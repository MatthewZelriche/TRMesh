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

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var structs = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                AttributeMetadataName,
                static (node, _) => node is StructDeclarationSyntax or RecordDeclarationSyntax,
                static (ctx, _) => GetModel(ctx)
            )
            .Where(static model => model is not null);

        context.RegisterSourceOutput(structs, static (spc, model) => Execute(spc, model!));
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

        return new SoAModel(
            structSymbol.Name,
            structSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            structSymbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : structSymbol.ContainingNamespace.ToDisplayString(),
            fieldModels,
            structSymbol.DeclaredAccessibility,
            IsGenericOrNestedInGenericType(structSymbol),
            context.TargetNode.GetLocation()
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
            Location? location
        )
        {
            StructName = structName;
            StructFullyQualifiedName = structFullyQualifiedName;
            Namespace = @namespace;
            Fields = fields;
            Accessibility = accessibility;
            IsGenericType = isGenericType;
            Location = location;
        }

        public string StructName { get; }
        public string StructFullyQualifiedName { get; }
        public string? Namespace { get; }
        public ImmutableArray<FieldModel> Fields { get; }
        public Accessibility Accessibility { get; }
        public bool IsGenericType { get; }
        public Location? Location { get; }
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
