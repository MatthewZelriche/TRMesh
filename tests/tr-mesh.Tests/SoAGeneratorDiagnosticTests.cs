using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TRMesh.Generators;

namespace tr_mesh.Tests;

public class SoAGeneratorDiagnosticTests
{
    const string AttributeSource = """
        using System;

        namespace TRMesh
        {
            [AttributeUsage(AttributeTargets.Struct)]
            public sealed class SoAAttribute : Attribute { }
        }

        """;

    static readonly MetadataReference[] FrameworkReferences = (
        (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!
    )
        .Split(Path.PathSeparator)
        .Select(static path => MetadataReference.CreateFromFile(path))
        .ToArray();

    [Theory]
    [InlineData("[TRMesh.SoA] public struct Sample<T> { public int Value; }", "TRMSOA003")]
    [InlineData("public class Outer<T> { [TRMesh.SoA] public struct Sample { public int Value; } }", "TRMSOA003")]
    [InlineData("[TRMesh.SoA] public struct Sample { }", "TRMSOA004")]
    [InlineData("[TRMesh.SoA] public record struct Sample;", "TRMSOA004")]
    [InlineData("[TRMesh.SoA] public struct Sample { public string Value; }", "TRMSOA002")]
    [InlineData("[TRMesh.SoA] public unsafe struct Sample { public int* Value; }", "TRMSOA002")]
    [InlineData("[TRMesh.SoA] public unsafe struct Sample { public fixed int Value[4]; }", "TRMSOA002")]
    [InlineData("[TRMesh.SoA] public unsafe struct Sample { public delegate*<void> Value; }", "TRMSOA002")]
    [InlineData("[TRMesh.SoA] public struct Sample { private int Value; }", "TRMSOA001")]
    public void UnsupportedStruct_ReportsExpectedDiagnostic(string declaration, string diagnosticId)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp12);
        var syntaxTree = CSharpSyntaxTree.ParseText(AttributeSource + declaration, parseOptions);
        var compilation = CSharpCompilation.Create(
            assemblyName: "SoAGeneratorDiagnosticTest",
            syntaxTrees: new[] { syntaxTree },
            references: FrameworkReferences,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true
            )
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new SoAGenerator().AsSourceGenerator() },
            parseOptions: parseOptions
        );

        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult();

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
        Assert.Empty(result.GeneratedTrees);
    }
}
