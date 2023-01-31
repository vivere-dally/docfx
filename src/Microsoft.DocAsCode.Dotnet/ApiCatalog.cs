// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.DocAsCode.Common;
using Microsoft.DocAsCode.Exceptions;

namespace Microsoft.DocAsCode.Metadata.ManagedReference
{
    public class ApiCatalog
    {
        static ApiCatalog()
        {
            var vs = MSBuildLocator.RegisterDefaults() ?? throw new DocfxException(
                $"Cannot find a supported .NET Core SDK. Install .NET Core SDK {Environment.Version.Major}.{Environment.Version.Minor}.x to build .NET API docs.");

            Logger.LogInfo($"Using {vs.Name} {vs.Version}");
        }

        public static async Task<ApiCatalog> Create(ExtractMetadataInputModel options)
        {
            var builder = new ApiCatalogBuilder();
            foreach (var file in options.Files)
            {
                switch (Path.GetExtension(file).ToLowerInvariant())
                {
                    case ".sln":
                        await CreateFromSolution(builder, file, options);
                        break;
                    case ".csproj":
                    case ".vbproj":
                        await CreateFromProject(builder, file, options);
                        break;
                    case ".dll":
                    case ".exe":
                        CreateFromAssembly(builder, file, options);
                        break;
                    case ".cs":
                    case ".vb":
                    default:
                        Logger.LogWarning($"Unknown .NET API file format: {file}");
                        break;
                }
            }
            return null;
        }

        public static void CreateFromSourceCode(ApiCatalogBuilder builder, string projectPath, ExtractMetadataInputModel options)
        {
        }

        public static async Task CreateFromSolution(ApiCatalogBuilder builder, string solutionPath, ExtractMetadataInputModel options)
        {
            Logger.LogInfo($"Loading solution {solutionPath}");

            using var workspace = MSBuildWorkspace.Create();
            var solution = await workspace.OpenSolutionAsync(solutionPath, new MsBuildLogger());

            foreach (var project in solution.Projects)
            {
                Logger.LogInfo($"  Loading project {project.FilePath}");

                var compilation = await project.GetCompilationAsync();
                await CreateFromCompilation(builder, compilation, options);
            }
        }

        public static async Task CreateFromProject(ApiCatalogBuilder builder, string projectPath, ExtractMetadataInputModel options)
        {
            Logger.LogInfo($"Loading project {projectPath}");

            using var workspace = MSBuildWorkspace.Create();
            var project = await workspace.OpenProjectAsync(projectPath, new MsBuildLogger());
            var compilation = await project.GetCompilationAsync();
            await CreateFromCompilation(builder, compilation, options);
        }

        public static void CreateFromAssembly(ApiCatalogBuilder builder, string solutionPath, ExtractMetadataInputModel options)
        {

        }

        private static async Task CreateFromCompilation(ApiCatalogBuilder builder, Compilation compilation, ExtractMetadataInputModel input)
        {
            foreach (var diagnostics in compilation.GetDiagnostics())
            {
                if (diagnostics.IsSuppressed)
                    continue;
                if (diagnostics.Severity is DiagnosticSeverity.Warning)
                    Logger.LogWarning($"    {diagnostics}");
                else if (diagnostics.Severity is DiagnosticSeverity.Error)
                    Logger.LogError($"    {diagnostics}");
            }

            var options = new ExtractMetadataOptions
            {
                ShouldSkipMarkup = input.ShouldSkipMarkup,
                PreserveRawInlineComments = input.PreserveRawInlineComments,
                FilterConfigFile = input.FilterConfigFile != null ? new FileInformation(input.FilterConfigFile).NormalizedPath : null,
                MSBuildProperties = input.MSBuildProperties,
                CodeSourceBasePath = input.CodeSourceBasePath,
                DisableDefaultFilter = input.DisableDefaultFilter,
                TocNamespaceStyle = input.TocNamespaceStyle
            };

            var i = new RoslynMetadataExtractor(compilation, compilation.Assembly).Extract(options);
        }

        class MsBuildLogger : ILogger
        {
            public LoggerVerbosity Verbosity { get; set; }
            public string Parameters { get; set; }

            public void Initialize(IEventSource eventSource)
            {
                eventSource.AnyEventRaised += (sender, e) =>
                {
                    Console.WriteLine(e.Message);
                };
            }

            public void Shutdown() { }
        }
    }
}
