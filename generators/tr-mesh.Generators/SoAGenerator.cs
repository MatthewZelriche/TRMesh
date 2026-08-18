using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TRMesh.Generators;

[Generator]
public sealed class SoAGenerator : IIncrementalGenerator
{
    const string AttributeMetadataName = "TRMesh.SoAAttribute";

    static readonly DiagnosticDescriptor InaccessibleFieldDiagnostic = new(
        id: "TRMSOA001",
        title: "SoA field is inaccessible",
        messageFormat: "Field '{0}' on struct '{1}' is not accessible to the generated SoA type",
        category: "TRMesh.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "SoA fields must be accessible from another type in the same assembly."
    );

    static readonly DiagnosticDescriptor UnsupportedFieldTypeDiagnostic = new(
        id: "TRMSOA002",
        title: "SoA field type is unsupported",
        messageFormat: "Field '{0}' on struct '{1}' has unsupported type '{2}'",
        category: "TRMesh.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "SoA fields must be ordinary unmanaged value types usable as generic type arguments."
    );

    static readonly DiagnosticDescriptor GenericStructDiagnostic = new(
        id: "TRMSOA003",
        title: "Generic SoA structs are unsupported",
        messageFormat: "Struct '{0}' is generic or nested in a generic type and is not supported by the SoA generator",
        category: "TRMesh.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    static readonly DiagnosticDescriptor EmptyStructDiagnostic = new(
        id: "TRMSOA004",
        title: "SoA struct has no fields",
        messageFormat: "Struct '{0}' must declare at least one instance field to generate SoA storage",
        category: "TRMesh.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    static readonly DiagnosticDescriptor InvalidTargetDiagnostic = new(
        id: "TRMSOA005",
        title: "SoA target is unsupported",
        messageFormat: "Type '{0}' must be a top-level, non-generic, non-static class declared in this compilation",
        category: "TRMesh.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    static readonly DiagnosticDescriptor NonPartialTargetDiagnostic = new(
        id: "TRMSOA006",
        title: "SoA target must be partial",
        messageFormat: "Class '{0}' must be declared partial so SoA members can be generated onto it",
        category: "TRMesh.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    static readonly DiagnosticDescriptor DuplicateFieldDiagnostic = new(
        id: "TRMSOA007",
        title: "SoA field name is ambiguous",
        messageFormat: "Field name '{0}' occurs more than once in the generated SoA hierarchy for '{1}'",
        category: "TRMesh.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var structs = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                AttributeMetadataName,
                static (node, _) => node is StructDeclarationSyntax or RecordDeclarationSyntax,
                static (ctx, _) => GetModel(ctx)
            )
            .Where(static model => model is not null);

        context.RegisterSourceOutput(
            structs.Where(static model => model!.TargetType is null),
            static (spc, model) => Execute(spc, model!)
        );

        context.RegisterSourceOutput(
            structs.Where(static model => model!.TargetType is not null).Collect(),
            static (spc, models) => ExecuteTargeted(spc, models)
        );
    }

    static SoAModel? GetModel(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol structSymbol)
            return null;

        if (structSymbol.TypeKind != TypeKind.Struct)
            return null;

        var fields = structSymbol
            .GetMembers()
            .OfType<IFieldSymbol>()
            .Where(static f => !f.IsStatic && !f.IsConst && !f.IsImplicitlyDeclared)
            .ToImmutableArray();

        var fieldModels = fields
            .Select(
                static (f, index) =>
                    new FieldModel(
                        f.Name,
                        EscapeIdentifier(f.Name),
                        $"_field{index}",
                        f.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        f.DeclaredAccessibility,
                        f.Locations.FirstOrDefault(),
                        f.Type.IsUnmanagedType
                            && f.Type is not IPointerTypeSymbol
                            && f.Type is not IFunctionPointerTypeSymbol
                            && !f.IsFixedSizeBuffer
                            && f.RefKind == RefKind.None,
                        IsColony: index == 0
                    )
            )
            .ToImmutableArray();

        INamedTypeSymbol? targetType = null;
        var attribute = context.Attributes.FirstOrDefault();
        if (
            attribute is not null
            && attribute.ConstructorArguments.Length == 1
            && attribute.ConstructorArguments[0].Value is INamedTypeSymbol onto
        )
        {
            targetType = onto;
        }
        return new SoAModel(
            structSymbol.Name,
            structSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            structSymbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : structSymbol.ContainingNamespace.ToDisplayString(),
            fieldModels,
            structSymbol.DeclaredAccessibility,
            IsGenericOrNestedInGenericType(structSymbol),
            context.TargetNode.GetLocation(),
            targetType
        );
    }

    static void Execute(SourceProductionContext context, SoAModel model)
    {
        if (model.IsGenericType)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    GenericStructDiagnostic,
                    model.Location,
                    model.StructFullyQualifiedName
                )
            );
            return;
        }

        if (model.Fields.Length == 0)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    EmptyStructDiagnostic,
                    model.Location,
                    model.StructFullyQualifiedName
                )
            );
            return;
        }

        var hasFieldErrors = false;
        foreach (var field in model.Fields)
        {
            if (!IsAccessibleFromGeneratedType(field.Accessibility))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        InaccessibleFieldDiagnostic,
                        field.Location,
                        field.Name,
                        model.StructFullyQualifiedName
                    )
                );
                hasFieldErrors = true;
            }

            if (!field.IsSupportedStorageType)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        UnsupportedFieldTypeDiagnostic,
                        field.Location,
                        field.Name,
                        model.StructFullyQualifiedName,
                        field.TypeDisplay
                    )
                );
                hasFieldErrors = true;
            }
        }

        if (hasFieldErrors)
            return;

        var source = GenerateSource(model);
        context.AddSource(SourceHintName(model), SourceText.From(source, Encoding.UTF8));
    }

    // Processes targeted components together so each class can share its generated ancestor's slots.
    static void ExecuteTargeted(
        SourceProductionContext context,
        ImmutableArray<SoAModel?> candidates
    )
    {
        var componentsByTarget = new Dictionary<INamedTypeSymbol, List<SoAModel>>(
            SymbolEqualityComparer.Default
        );

        foreach (var candidate in candidates)
        {
            if (candidate is null || candidate.TargetType is null)
                continue;

            if (!componentsByTarget.TryGetValue(candidate.TargetType, out var components))
            {
                components = [];
                componentsByTarget.Add(candidate.TargetType, components);
            }

            components.Add(candidate);
        }

        var targets = new Dictionary<INamedTypeSymbol, TargetModel>(SymbolEqualityComparer.Default);
        foreach (var pair in componentsByTarget)
        {
            var firstComponent = pair.Value[0];
            var canGenerate = ValidateTarget(context, pair.Key, firstComponent.Location);
            foreach (var component in pair.Value)
                canGenerate &= ValidateComponent(context, component);

            pair.Value.Sort(
                static (left, right) =>
                    string.CompareOrdinal(
                        left.StructFullyQualifiedName,
                        right.StructFullyQualifiedName
                    )
            );
            var target = new TargetModel(pair.Key, pair.Value.ToImmutableArray())
            {
                CanGenerate = canGenerate,
            };
            targets.Add(pair.Key, target);
        }

        foreach (var target in targets.Values)
            target.GeneratedBase = FindGeneratedBase(target.Symbol.BaseType, targets);

        foreach (var target in targets.Values)
        {
            if (target.CanGenerate && !ValidateUniqueHierarchyFields(context, target))
                target.CanGenerate = false;
        }

        // A descendant cannot safely become a new slot owner merely because an annotated ancestor
        // failed validation. Suppress it along with the invalid ancestor.
        foreach (var target in targets.Values)
        {
            for (
                var ancestor = target.GeneratedBase;
                ancestor is not null;
                ancestor = ancestor.GeneratedBase
            )
            {
                if (!ancestor.CanGenerate)
                {
                    target.CanGenerate = false;
                    break;
                }
            }
        }

        foreach (var target in targets.Values)
        {
            if (!target.CanGenerate)
                continue;

            var source = GenerateTargetSource(target);
            context.AddSource(TargetSourceHintName(target), SourceText.From(source, Encoding.UTF8));
        }
    }

    static bool ValidateComponent(SourceProductionContext context, SoAModel model)
    {
        var valid = true;
        if (model.IsGenericType)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    GenericStructDiagnostic,
                    model.Location,
                    model.StructFullyQualifiedName
                )
            );
            valid = false;
        }

        if (model.Fields.Length == 0)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    EmptyStructDiagnostic,
                    model.Location,
                    model.StructFullyQualifiedName
                )
            );
            valid = false;
        }

        foreach (var field in model.Fields)
        {
            if (!IsAccessibleFromGeneratedType(field.Accessibility))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        InaccessibleFieldDiagnostic,
                        field.Location,
                        field.Name,
                        model.StructFullyQualifiedName
                    )
                );
                valid = false;
            }

            if (!field.IsSupportedStorageType)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        UnsupportedFieldTypeDiagnostic,
                        field.Location,
                        field.Name,
                        model.StructFullyQualifiedName,
                        field.TypeDisplay
                    )
                );
                valid = false;
            }
        }

        return valid;
    }

    static bool ValidateTarget(
        SourceProductionContext context,
        INamedTypeSymbol target,
        Location? location
    )
    {
        var isSupported =
            target.TypeKind == TypeKind.Class
            && !target.IsStatic
            && !target.IsRecord
            && target.Arity == 0
            && target.ContainingType is null
            && target.DeclaringSyntaxReferences.Length != 0;

        if (!isSupported)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    InvalidTargetDiagnostic,
                    location,
                    target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                )
            );
            return false;
        }

        var isPartial = target.DeclaringSyntaxReferences.All(static syntaxReference =>
            syntaxReference.GetSyntax() is ClassDeclarationSyntax declaration
            && declaration.Modifiers.Any(SyntaxKind.PartialKeyword)
        );
        if (!isPartial)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    NonPartialTargetDiagnostic,
                    location,
                    target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                )
            );
            return false;
        }

        return true;
    }

    static TargetModel? FindGeneratedBase(
        INamedTypeSymbol? baseType,
        Dictionary<INamedTypeSymbol, TargetModel> targets
    )
    {
        for (var current = baseType; current is not null; current = current.BaseType)
        {
            if (targets.TryGetValue(current, out var generatedBase))
                return generatedBase;
        }

        return null;
    }

    static bool ValidateUniqueHierarchyFields(SourceProductionContext context, TargetModel target)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var valid = true;
        foreach (var hierarchyTarget in GetHierarchy(target))
        {
            foreach (var field in hierarchyTarget.Fields)
            {
                if (names.Add(field.Field.Name))
                    continue;

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        DuplicateFieldDiagnostic,
                        field.Field.Location,
                        field.Field.Name,
                        target.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    )
                );
                valid = false;
            }
        }

        return valid;
    }

    static ImmutableArray<TargetModel> GetHierarchy(TargetModel target)
    {
        var stack = new Stack<TargetModel>();
        for (var current = target; current is not null; current = current.GeneratedBase)
            stack.Push(current);

        var builder = ImmutableArray.CreateBuilder<TargetModel>(stack.Count);
        while (stack.Count != 0)
            builder.Add(stack.Pop());
        return builder.MoveToImmutable();
    }

    static string TargetSourceHintName(TargetModel target) =>
        $"{target.Symbol.Name}.SoA.{StableId(target.FullyQualifiedName)}.g.cs";

    static string GenerateTargetSource(TargetModel target)
    {
        var hierarchy = GetHierarchy(target);
        var root = hierarchy[0];
        var rootAnchor = root.Fields[0];
        var allFields = hierarchy.SelectMany(static item => item.Fields).ToArray();
        var allComponents = hierarchy.SelectMany(static item => item.Components).ToArray();
        var parameterNames = CreateParameterNames(allComponents);
        var viewName = UniqueNestedTypeName(
            "View",
            allFields.Select(static item => item.Field).ToImmutableArray()
        );
        var isRoot = target.GeneratedBase is null;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using TRMesh.Containers;");
        sb.AppendLine();

        if (target.Namespace is not null)
        {
            sb.Append("namespace ").Append(target.Namespace).AppendLine(";");
            sb.AppendLine();
        }

        sb.Append(AccessibilityText(target.Symbol.DeclaredAccessibility))
            .Append(" partial class ")
            .Append(EscapeIdentifier(target.Symbol.Name));
        if (isRoot)
            sb.Append(" : IDisposable");
        sb.AppendLine();
        sb.AppendLine("{");

        foreach (var field in target.Fields)
        {
            sb.Append("    private ");
            if (isRoot && ReferenceEquals(field, rootAnchor))
                sb.Append("UnsafeColony<");
            else
                sb.Append("UnsafeChunkedList<");
            sb.Append(field.Field.TypeDisplay)
                .Append("> ")
                .Append(field.StorageName)
                .AppendLine(";");
        }
        sb.AppendLine();

        foreach (var field in target.Fields)
        {
            sb.AppendLine("    [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.Append("    protected ref ")
                .Append(field.Field.TypeDisplay)
                .Append(' ')
                .Append(field.AccessorName)
                .Append("(int slot) => ref ")
                .Append(field.StorageName)
                .AppendLine("[slot];");
            sb.AppendLine();
        }

        if (isRoot)
            AppendRootTargetMembers(sb, target, rootAnchor, parameterNames);
        else
            AppendDerivedTargetMembers(sb, target, parameterNames);

        AppendTargetView(sb, target, allFields, viewName, isRoot);
        AppendTargetEnumerator(sb, target, rootAnchor, viewName, isRoot);

        sb.AppendLine("}");
        return sb.ToString();
    }

    static void AppendRootTargetMembers(
        StringBuilder sb,
        TargetModel target,
        TargetField rootAnchor,
        Dictionary<SoAModel, string> parameterNames
    )
    {
        sb.AppendLine("    public int Count");
        sb.AppendLine("    {");
        sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.Append("        get => ").Append(rootAnchor.StorageName).AppendLine(".Count;");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.Append("    public bool IsAlive(int slot) => ")
            .Append(rootAnchor.StorageName)
            .AppendLine(".IsActive(slot);");
        sb.AppendLine();

        AppendDefaultInsert(sb, target.Components, isDerived: false);
        AppendInsertSignature(sb, target.Components, parameterNames);
        sb.AppendLine("    {");
        sb.Append("        int slot = ")
            .Append(rootAnchor.StorageName)
            .Append(".Insert(")
            .Append(FieldValueExpression(rootAnchor, parameterNames))
            .AppendLine(");");
        foreach (var field in target.Fields)
        {
            if (ReferenceEquals(field, rootAnchor))
                continue;
            sb.Append("        ")
                .Append(field.StorageName)
                .Append("[slot] = ")
                .Append(FieldValueExpression(field, parameterNames))
                .AppendLine(";");
        }
        sb.AppendLine("        __SoAInitializeExtendedColumns(slot);");
        sb.AppendLine("        return slot;");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    protected virtual void __SoAInitializeExtendedColumns(int slot) { }");
        sb.AppendLine();

        sb.AppendLine("    public void RemoveAt(int slot)");
        sb.AppendLine("    {");
        sb.Append("        ").Append(rootAnchor.StorageName).AppendLine(".RemoveAt(slot);");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    public void Clear()");
        sb.AppendLine("    {");
        sb.Append("        ").Append(rootAnchor.StorageName).AppendLine(".Clear();");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    public void Dispose()");
        sb.AppendLine("    {");
        sb.AppendLine("        __SoADisposeColumns();");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    protected virtual void __SoADisposeColumns()");
        sb.AppendLine("    {");
        foreach (var field in target.Fields)
            sb.Append("        ").Append(field.StorageName).AppendLine(".Dispose();");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.Append("    protected UnsafeColony<")
            .Append(rootAnchor.Field.TypeDisplay)
            .Append(">.Enumerator __SoAGetSlotEnumerator() => ")
            .Append(rootAnchor.StorageName)
            .AppendLine(".GetEnumerator();");
        sb.AppendLine();
    }

    static void AppendDerivedTargetMembers(
        StringBuilder sb,
        TargetModel target,
        Dictionary<SoAModel, string> parameterNames
    )
    {
        var baseHierarchy = GetHierarchy(target.GeneratedBase!);
        var baseComponents = baseHierarchy
            .SelectMany(static item => item.Components)
            .ToImmutableArray();
        var allComponents = baseComponents.AddRange(target.Components);

        AppendDefaultInsert(sb, allComponents, isDerived: true);

        var inheritedComponents = ImmutableArray<SoAModel>.Empty;
        foreach (var baseTarget in baseHierarchy)
        {
            inheritedComponents = inheritedComponents.AddRange(baseTarget.Components);
            AppendForwardingInsert(sb, inheritedComponents, parameterNames);
        }

        AppendInsertSignature(sb, allComponents, parameterNames);
        sb.AppendLine("    {");
        sb.Append("        int slot = base.Insert(");
        for (var i = 0; i < baseComponents.Length; i++)
        {
            if (i != 0)
                sb.Append(", ");
            sb.Append(parameterNames[baseComponents[i]]);
        }
        sb.AppendLine(");");
        foreach (var field in target.Fields)
        {
            sb.Append("        ")
                .Append(field.StorageName)
                .Append("[slot] = ")
                .Append(FieldValueExpression(field, parameterNames))
                .AppendLine(";");
        }
        sb.AppendLine("        return slot;");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    protected override void __SoAInitializeExtendedColumns(int slot)");
        sb.AppendLine("    {");
        sb.AppendLine("        base.__SoAInitializeExtendedColumns(slot);");
        foreach (var field in target.Fields)
            sb.Append("        ").Append(field.StorageName).AppendLine("[slot] = default;");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    protected override void __SoADisposeColumns()");
        sb.AppendLine("    {");
        foreach (var field in target.Fields)
            sb.Append("        ").Append(field.StorageName).AppendLine(".Dispose();");
        sb.AppendLine("        base.__SoADisposeColumns();");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    static void AppendDefaultInsert(
        StringBuilder sb,
        ImmutableArray<SoAModel> components,
        bool isDerived
    )
    {
        sb.Append("    public ");
        if (isDerived)
            sb.Append("new ");
        sb.AppendLine("int Insert()");
        sb.AppendLine("    {");
        sb.Append("        return Insert(");
        for (var i = 0; i < components.Length; i++)
        {
            if (i != 0)
                sb.Append(", ");
            sb.Append("default");
        }
        sb.AppendLine(");");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    static void AppendForwardingInsert(
        StringBuilder sb,
        ImmutableArray<SoAModel> components,
        Dictionary<SoAModel, string> parameterNames
    )
    {
        sb.Append("    ").Append(InsertAccessibility(components)).Append(" new int Insert(");
        AppendInsertParameters(sb, components, parameterNames);
        sb.AppendLine(")");
        sb.AppendLine("    {");
        sb.Append("        return base.Insert(");
        for (var i = 0; i < components.Length; i++)
        {
            if (i != 0)
                sb.Append(", ");
            sb.Append(parameterNames[components[i]]);
        }
        sb.AppendLine(");");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    static void AppendInsertSignature(
        StringBuilder sb,
        ImmutableArray<SoAModel> components,
        Dictionary<SoAModel, string> parameterNames
    )
    {
        sb.Append("    ").Append(InsertAccessibility(components)).Append(" int Insert(");
        AppendInsertParameters(sb, components, parameterNames);
        sb.AppendLine(")");
    }

    static void AppendInsertParameters(
        StringBuilder sb,
        ImmutableArray<SoAModel> components,
        Dictionary<SoAModel, string> parameterNames
    )
    {
        for (var i = 0; i < components.Length; i++)
        {
            if (i != 0)
                sb.Append(", ");
            sb.Append(components[i].StructFullyQualifiedName)
                .Append(' ')
                .Append(parameterNames[components[i]]);
        }
    }

    static string InsertAccessibility(ImmutableArray<SoAModel> components) =>
        components.All(static component => component.Accessibility == Accessibility.Public)
            ? "public"
            : "internal";

    static void AppendTargetView(
        StringBuilder sb,
        TargetModel target,
        TargetField[] allFields,
        string viewName,
        bool isRoot
    )
    {
        sb.AppendLine("    [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.Append("    public ");
        if (!isRoot)
            sb.Append("new ");
        sb.Append(viewName)
            .Append(" Get(int slot) => new ")
            .Append(viewName)
            .AppendLine("(this, slot);");
        sb.AppendLine();

        sb.Append("    public ");
        if (!isRoot)
            sb.Append("new ");
        sb.Append("readonly ref struct ").AppendLine(viewName);
        sb.AppendLine("    {");
        foreach (var field in allFields)
        {
            sb.Append("        ")
                .Append(FieldAccessibilityText(field.Field.Accessibility))
                .Append(" readonly ref ")
                .Append(field.Field.TypeDisplay)
                .Append(' ')
                .Append(field.Field.EscapedName)
                .AppendLine(";");
        }
        sb.AppendLine();
        sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.Append("        internal ")
            .Append(viewName)
            .Append('(')
            .Append(target.FullyQualifiedName)
            .AppendLine(" soa, int slot)");
        sb.AppendLine("        {");
        foreach (var field in allFields)
        {
            sb.Append("            this.")
                .Append(field.Field.EscapedName)
                .Append(" = ref soa.")
                .Append(field.AccessorName)
                .AppendLine("(slot);");
        }
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    static void AppendTargetEnumerator(
        StringBuilder sb,
        TargetModel target,
        TargetField rootAnchor,
        string viewName,
        bool isRoot
    )
    {
        sb.AppendLine("    [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.Append("    public ");
        if (!isRoot)
            sb.Append("new ");
        sb.AppendLine(
            "Enumerator GetEnumerator() => new Enumerator(this, __SoAGetSlotEnumerator());"
        );
        sb.AppendLine();

        sb.Append("    public ");
        if (!isRoot)
            sb.Append("new ");
        sb.AppendLine("ref struct Enumerator");
        sb.AppendLine("    {");
        sb.Append("        private readonly ")
            .Append(target.FullyQualifiedName)
            .AppendLine(" _soa;");
        sb.Append("        private UnsafeColony<")
            .Append(rootAnchor.Field.TypeDisplay)
            .AppendLine(">.Enumerator _inner;");
        sb.AppendLine();
        sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.Append("        internal Enumerator(")
            .Append(target.FullyQualifiedName)
            .Append(" soa, UnsafeColony<")
            .Append(rootAnchor.Field.TypeDisplay)
            .AppendLine(">.Enumerator inner)");
        sb.AppendLine("        {");
        sb.AppendLine("            _soa = soa;");
        sb.AppendLine("            _inner = inner;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.Append("        public ").Append(viewName).AppendLine(" Current");
        sb.AppendLine("        {");
        sb.AppendLine("            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.Append("            get => new ")
            .Append(viewName)
            .AppendLine("(_soa, _inner.CurrentSlot);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public int CurrentSlot");
        sb.AppendLine("        {");
        sb.AppendLine("            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine("            get => _inner.CurrentSlot;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine("        public bool MoveNext() => _inner.MoveNext();");
        sb.AppendLine("    }");
    }

    static Dictionary<SoAModel, string> CreateParameterNames(SoAModel[] components)
    {
        var result = new Dictionary<SoAModel, string>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in components)
        {
            var preferred =
                component.StructName.Length == 0
                    ? "value"
                    : char.ToLowerInvariant(component.StructName[0])
                        + component.StructName.Substring(1);
            var name = preferred;
            var suffix = 2;
            while (!used.Add(name))
                name = preferred + suffix++;
            result.Add(component, EscapeIdentifier(name));
        }
        return result;
    }

    static string FieldValueExpression(
        TargetField field,
        Dictionary<SoAModel, string> parameterNames
    ) => parameterNames[field.Component] + "." + field.Field.EscapedName;

    static string StableId(string identity)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(identity));
        var suffix = new StringBuilder(16);
        for (var i = 0; i < 8; i++)
            suffix.Append(hash[i].ToString("x2"));
        return suffix.ToString();
    }

    static string SourceHintName(SoAModel model)
    {
        // AddSource requires every hint name from this generator to be unique. Hash the complete
        // symbol identity so equal simple names in different namespaces or containing types do not
        // collide, while keeping the physical generated-file name short.
        using var sha256 = SHA256.Create();
        var identity = Encoding.UTF8.GetBytes(model.StructFullyQualifiedName);
        var hash = sha256.ComputeHash(identity);
        var suffix = new StringBuilder(hash.Length * 2);
        foreach (var value in hash)
            suffix.Append(value.ToString("x2"));

        return $"{model.StructName}SoA.{suffix}.g.cs";
    }

    static string GenerateSource(SoAModel model)
    {
        var sb = new StringBuilder();
        var soaName = $"{model.StructName}SoA";
        var accessibility = AccessibilityText(model.Accessibility);
        var colony = model.Fields[0];
        var parallel = model.Fields.Skip(1).ToArray();
        var viewName = UniqueNestedTypeName("View", model.Fields);

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using TRMesh.Containers;");
        sb.AppendLine();

        if (model.Namespace is not null)
        {
            sb.Append("namespace ").Append(model.Namespace).AppendLine(";");
            sb.AppendLine();
        }

        sb.Append(accessibility)
            .Append(" sealed class ")
            .Append(soaName)
            .AppendLine(" : IDisposable");
        sb.AppendLine("{");

        foreach (var field in model.Fields)
        {
            if (field.IsColony)
            {
                sb.Append("    private UnsafeColony<")
                    .Append(field.TypeDisplay)
                    .Append("> ")
                    .Append(field.StorageName)
                    .AppendLine(";");
            }
            else
            {
                sb.Append("    private UnsafeChunkedList<")
                    .Append(field.TypeDisplay)
                    .Append("> ")
                    .Append(field.StorageName)
                    .AppendLine(";");
            }
        }

        sb.AppendLine();
        sb.Append("    public ").Append(soaName).AppendLine("()");
        sb.AppendLine("    {");
        sb.Append("        ")
            .Append(colony.StorageName)
            .Append(" = new UnsafeColony<")
            .Append(colony.TypeDisplay)
            .AppendLine(">();");
        foreach (var field in parallel)
        {
            sb.Append("        ")
                .Append(field.StorageName)
                .Append(" = new UnsafeChunkedList<")
                .Append(field.TypeDisplay)
                .AppendLine(">();");
        }
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    public int Count");
        sb.AppendLine("    {");
        sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.Append("        get => ").Append(colony.StorageName).AppendLine(".Count;");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.Append("    public bool IsAlive(int slot) => ")
            .Append(colony.StorageName)
            .AppendLine(".IsActive(slot);");
        sb.AppendLine();

        sb.Append("    public int Insert() => Insert(default(")
            .Append(model.StructFullyQualifiedName)
            .AppendLine("));");
        sb.AppendLine();

        sb.Append("    public int Insert(")
            .Append(model.StructFullyQualifiedName)
            .AppendLine(" value)");
        sb.AppendLine("    {");
        sb.Append("        int slot = ")
            .Append(colony.StorageName)
            .Append(".Insert(value.")
            .Append(colony.EscapedName)
            .AppendLine(");");
        if (parallel.Length > 0)
        {
            foreach (var field in parallel)
            {
                sb.Append("        ")
                    .Append(field.StorageName)
                    .Append("[slot] = value.")
                    .Append(field.EscapedName)
                    .AppendLine(";");
            }
        }
        sb.AppendLine("        return slot;");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    /// <summary>Removes the element at the specified slot.</summary>");
        sb.AppendLine(
            "    /// <remarks>References exposed by a view for this element are invalid after removal.</remarks>"
        );
        sb.AppendLine("    public void RemoveAt(int slot)");
        sb.AppendLine("    {");
        sb.Append("        ").Append(colony.StorageName).AppendLine(".RemoveAt(slot);");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine(
            "    /// <summary>Removes all elements while retaining allocated storage.</summary>"
        );
        sb.AppendLine(
            "    /// <remarks>All references exposed by existing views are invalid after this call.</remarks>"
        );
        sb.AppendLine("    public void Clear()");
        sb.AppendLine("    {");
        sb.Append("        ").Append(colony.StorageName).AppendLine(".Clear();");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine(
            "    /// <summary>Releases all native storage owned by this collection.</summary>"
        );
        sb.AppendLine(
            "    /// <remarks>All references exposed by existing views are invalid after this call.</remarks>"
        );
        sb.AppendLine("    public void Dispose()");
        sb.AppendLine("    {");
        foreach (var field in model.Fields)
        {
            sb.Append("        ").Append(field.StorageName).AppendLine(".Dispose();");
        }
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine(
            "    /// <summary>Returns a mutable view of the element at the specified slot.</summary>"
        );
        sb.AppendLine("    /// <remarks>");
        sb.AppendLine(
            "    /// References exposed by the view remain valid across insertions, but become invalid when"
        );
        sb.AppendLine(
            "    /// the element is removed or when the collection is cleared or disposed."
        );
        sb.AppendLine("    /// </remarks>");
        sb.AppendLine("    [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.Append("    public ")
            .Append(viewName)
            .Append(" Get(int slot) => new ")
            .Append(viewName)
            .AppendLine("(this, slot);");
        sb.AppendLine();

        sb.AppendLine("    [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.Append("    public Enumerator GetEnumerator() => new Enumerator(this, ")
            .Append(colony.StorageName)
            .AppendLine(".GetEnumerator());");
        sb.AppendLine();

        // View
        sb.AppendLine(
            "    /// <summary>Mutable references to the fields of one element.</summary>"
        );
        sb.AppendLine("    /// <remarks>");
        sb.AppendLine(
            "    /// Field references remain valid across insertions. They are invalid after the element is"
        );
        sb.AppendLine("    /// removed or after the owning collection is cleared or disposed.");
        sb.AppendLine("    /// </remarks>");
        sb.Append("    public readonly ref struct ").AppendLine(viewName);
        sb.AppendLine("    {");
        foreach (var field in model.Fields)
        {
            // readonly ref: cannot rebind outside the ctor; referent remains mutable.
            sb.Append("        ")
                .Append(FieldAccessibilityText(field.Accessibility))
                .Append(" readonly ref ")
                .Append(field.TypeDisplay)
                .Append(' ')
                .Append(field.EscapedName)
                .AppendLine(";");
        }
        sb.AppendLine();
        sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.Append("        internal ")
            .Append(viewName)
            .Append('(')
            .Append(soaName)
            .AppendLine(" soa, int slot)");
        sb.AppendLine("        {");
        foreach (var field in model.Fields)
        {
            sb.Append("            this.")
                .Append(field.EscapedName)
                .Append(" = ref soa.")
                .Append(field.StorageName)
                .AppendLine("[slot];");
        }
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        // Enumerator
        sb.AppendLine("    public ref struct Enumerator");
        sb.AppendLine("    {");
        sb.Append("        private readonly ").Append(soaName).AppendLine(" _soa;");
        sb.Append("        private UnsafeColony<")
            .Append(colony.TypeDisplay)
            .AppendLine(">.Enumerator _inner;");
        sb.AppendLine();
        sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.Append("        internal Enumerator(")
            .Append(soaName)
            .Append(" soa, UnsafeColony<")
            .Append(colony.TypeDisplay)
            .AppendLine(">.Enumerator inner)");
        sb.AppendLine("        {");
        sb.AppendLine("            _soa = soa;");
        sb.AppendLine("            _inner = inner;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.Append("        public ").Append(viewName).AppendLine(" Current");
        sb.AppendLine("        {");
        sb.AppendLine("            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.Append("            get => new ")
            .Append(viewName)
            .AppendLine("(_soa, _inner.CurrentSlot);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public int CurrentSlot");
        sb.AppendLine("        {");
        sb.AppendLine("            [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine("            get => _inner.CurrentSlot;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine("        public bool MoveNext() => _inner.MoveNext();");
        sb.AppendLine("    }");

        sb.AppendLine("}");
        return sb.ToString();
    }

    static string EscapeIdentifier(string identifier) =>
        SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None
        || SyntaxFacts.GetContextualKeywordKind(identifier) != SyntaxKind.None
            ? "@" + identifier
            : identifier;

    static string UniqueNestedTypeName(string preferredName, ImmutableArray<FieldModel> fields)
    {
        var name = preferredName;
        while (fields.Any(field => field.Name == name))
            name += "_";
        return name;
    }

    static bool IsGenericOrNestedInGenericType(INamedTypeSymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingType)
        {
            if (current.Arity != 0)
                return true;
        }

        return false;
    }

    static string AccessibilityText(Accessibility accessibility) =>
        accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "public",
            Accessibility.ProtectedOrInternal => "public",
            Accessibility.ProtectedAndInternal => "internal",
            Accessibility.Private => "internal",
            _ => "public",
        };

    static bool IsAccessibleFromGeneratedType(Accessibility accessibility) =>
        accessibility
            is Accessibility.Public
                or Accessibility.Internal
                or Accessibility.ProtectedOrInternal;

    static string FieldAccessibilityText(Accessibility accessibility) =>
        accessibility == Accessibility.Public ? "public" : "internal";

    // Stores information about the struct that is being generated, for emission purposes.
    // Later consumed by the Execute method to generate the source code.
    sealed class SoAModel
    {
        public SoAModel(
            string structName,
            string structFullyQualifiedName,
            string? @namespace,
            ImmutableArray<FieldModel> fields,
            Accessibility accessibility,
            bool isGenericType,
            Location? location,
            INamedTypeSymbol? targetType
        )
        {
            StructName = structName;
            StructFullyQualifiedName = structFullyQualifiedName;
            Namespace = @namespace;
            Fields = fields;
            Accessibility = accessibility;
            IsGenericType = isGenericType;
            Location = location;
            TargetType = targetType;
        }

        public string StructName { get; }
        public string StructFullyQualifiedName { get; }
        public string? Namespace { get; }
        public ImmutableArray<FieldModel> Fields { get; }
        public Accessibility Accessibility { get; }
        public bool IsGenericType { get; }
        public Location? Location { get; }
        public INamedTypeSymbol? TargetType { get; }
    }

    sealed class TargetModel
    {
        public TargetModel(INamedTypeSymbol symbol, ImmutableArray<SoAModel> components)
        {
            Symbol = symbol;
            Components = components;
            FullyQualifiedName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            Namespace = symbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : symbol.ContainingNamespace.ToDisplayString();

            var id = StableId(FullyQualifiedName);
            var fields = ImmutableArray.CreateBuilder<TargetField>();
            var fieldIndex = 0;
            foreach (var component in components)
            {
                foreach (var field in component.Fields)
                {
                    fields.Add(
                        new TargetField(
                            component,
                            field,
                            $"__soa_{id}_field{fieldIndex}",
                            $"__SoA_{id}_GetField{fieldIndex}"
                        )
                    );
                    fieldIndex++;
                }
            }
            Fields = fields.ToImmutable();
        }

        public INamedTypeSymbol Symbol { get; }
        public string FullyQualifiedName { get; }
        public string? Namespace { get; }
        public ImmutableArray<SoAModel> Components { get; }
        public ImmutableArray<TargetField> Fields { get; }
        public TargetModel? GeneratedBase { get; set; }
        public bool CanGenerate { get; set; } = true;
    }

    sealed class TargetField
    {
        public TargetField(
            SoAModel component,
            FieldModel field,
            string storageName,
            string accessorName
        )
        {
            Component = component;
            Field = field;
            StorageName = storageName;
            AccessorName = accessorName;
        }

        public SoAModel Component { get; }
        public FieldModel Field { get; }
        public string StorageName { get; }
        public string AccessorName { get; }
    }

    sealed class FieldModel
    {
        public FieldModel(
            string name,
            string escapedName,
            string storageName,
            string typeDisplay,
            Accessibility accessibility,
            Location? location,
            bool isSupportedStorageType,
            bool IsColony
        )
        {
            Name = name;
            EscapedName = escapedName;
            StorageName = storageName;
            TypeDisplay = typeDisplay;
            Accessibility = accessibility;
            Location = location;
            IsSupportedStorageType = isSupportedStorageType;
            this.IsColony = IsColony;
        }

        public string Name { get; }
        public string EscapedName { get; }
        public string StorageName { get; }
        public string TypeDisplay { get; }
        public Accessibility Accessibility { get; }
        public Location? Location { get; }
        public bool IsSupportedStorageType { get; }
        public bool IsColony { get; }
    }
}
