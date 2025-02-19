﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.DocAsCode.Metadata.ManagedReference
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using ICSharpCode.Decompiler.Metadata;
    using Microsoft.CodeAnalysis;
    using Microsoft.DocAsCode.Common;
    using Microsoft.DocAsCode.Exceptions;
    using CS = Microsoft.CodeAnalysis.CSharp;
    using VB = Microsoft.CodeAnalysis.VisualBasic;

    internal static class CompilationUtility
    {
        public static Compilation CreateCompilationFromCSharpFiles(IEnumerable<string> files)
        {
            return CS.CSharpCompilation.Create(
                assemblyName: "cs.temp.dll",
                options: new CS.CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, xmlReferenceResolver: XmlFileResolver.Default),
                syntaxTrees: files.Select(path => CS.SyntaxFactory.ParseSyntaxTree(File.ReadAllText(path), path: path)),
                references: GetDefaultMetadataReferences("C#"));
        }

        public static Compilation CreateCompilationFromCSharpCode(string code, string name = "cs.temp.dll", params MetadataReference[] references)
        {
            return CS.CSharpCompilation.Create(
                name,
                options: new CS.CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, xmlReferenceResolver: XmlFileResolver.Default),
                syntaxTrees: new[] { CS.SyntaxFactory.ParseSyntaxTree(code) },
                references: GetDefaultMetadataReferences("C#").Concat(references));
        }

        public static Compilation CreateCompilationFromVBFiles(IEnumerable<string> files)
        {
            return VB.VisualBasicCompilation.Create(
                assemblyName: "vb.temp.dll",
                options: new VB.VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, xmlReferenceResolver: XmlFileResolver.Default),
                syntaxTrees: files.Select(path => VB.SyntaxFactory.ParseSyntaxTree(File.ReadAllText(path), path: path)),
                references: GetDefaultMetadataReferences("VB"));
        }

        public static Compilation CreateCompilationFromVBCode(string code, string name = "vb.temp.dll", params MetadataReference[] references)
        {
            return VB.VisualBasicCompilation.Create(
                name,
                options: new VB.VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, xmlReferenceResolver: XmlFileResolver.Default),
                syntaxTrees: new[] { VB.SyntaxFactory.ParseSyntaxTree(code) },
                references: GetDefaultMetadataReferences("VB").Concat(references));
        }

        public static Compilation CreateCompilationFromAssembly(IEnumerable<string> assemblyPaths, IEnumerable<string> references = null)
        {
            return CS.CSharpCompilation.Create(
                "EmptyProjectWithAssembly",
                options: new CS.CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                syntaxTrees: Array.Empty<SyntaxTree>(),
                references: assemblyPaths
                    .Concat(assemblyPaths.SelectMany(GetReferenceAssemblies))
                    .Concat(references ?? Enumerable.Empty<string>())
                    .Select(CreateMetadataReference));
        }

        public static IEnumerable<IAssemblySymbol> GetAssemblyFromAssemblyComplation(Compilation assemblyCompilation, IReadOnlyCollection<string> assemblyPaths)
        {
            foreach (var reference in assemblyCompilation.References)
            {
                Logger.LogVerbose($"Loading assembly {reference.Display}...");
                var assembly = (IAssemblySymbol)assemblyCompilation.GetAssemblyOrModuleSymbol(reference);
                if (assembly == null)
                {
                    Logger.LogWarning($"Unable to get symbol from {reference.Display}, ignored...");
                    continue;
                }

                if (reference is PortableExecutableReference portableReference &&
                    assemblyPaths.Any(path => portableReference.FilePath.Replace('\\', '/') == path.Replace('\\', '/')))
                {
                    yield return assembly;
                }
            }
        }

        private static IEnumerable<MetadataReference> GetDefaultMetadataReferences(string language)
        {
            try
            {
                var dotnetExeDirectory = DotNetCorePathFinder.FindDotNetExeDirectory();
                var refDirectory = Path.Combine(dotnetExeDirectory, "packs/Microsoft.NETCore.App.Ref");
                var version = new DirectoryInfo(refDirectory).GetDirectories().Select(d => d.Name).Max();
                var moniker = new DirectoryInfo(Path.Combine(refDirectory, version, "ref")).GetDirectories().Select(d => d.Name).Max();
                var path = Path.Combine(refDirectory, version, "ref", moniker);

                Logger.LogInfo($"Compiling {language} files using .NET SDK {version} for {moniker}");
                return Directory.EnumerateFiles(path, "*.dll", SearchOption.TopDirectoryOnly)
                                .Select(CreateMetadataReference);
            }
            catch
            {
                throw new DocfxException("Cannot find .NET Core SDK to compile the project.");
            }
        }

        private static IEnumerable<string> GetReferenceAssemblies(string assemblyPath)
        {
            using var assembly = new PEFile(assemblyPath);
            var assemblyResolver = new UniversalAssemblyResolver(assemblyPath, false, assembly.DetectTargetFrameworkId());
            foreach (var reference in assembly.AssemblyReferences)
            {
                if (assemblyResolver.FindAssemblyFile(reference) is { } file)
                    yield return file;
            }
        }

        private static MetadataReference CreateMetadataReference(string assemblyPath)
        {
            var documentation = XmlDocumentationProvider.CreateFromFile(Path.ChangeExtension(assemblyPath, ".xml"));
            return MetadataReference.CreateFromFile(assemblyPath, documentation: documentation);
        }
    }
}
