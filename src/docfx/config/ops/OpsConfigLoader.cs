// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

#nullable enable

namespace Microsoft.Docs.Build
{
    internal static class OpsConfigLoader
    {
        public static OpsConfig? LoadOpsConfig(string workingDirectory)
        {
            var fullPath = Path.Combine(workingDirectory, ".openpublishing.publish.config.json");
            if (!File.Exists(fullPath))
            {
                return null;
            }

            var filePath = new FilePath(Path.GetRelativePath(workingDirectory, fullPath));
            return JsonUtility.Deserialize<OpsConfig>(File.ReadAllText(fullPath), filePath);
        }

        public static (string? xrefEndpoint, string[]? xrefQueryTags, JObject? config) LoadDocfxConfig(string docsetPath, Repository? repository)
        {
            if (repository is null)
            {
                return default;
            }

            var opsConfig = LoadOpsConfig(repository.Path);
            if (opsConfig is null)
            {
                return default;
            }

            var buildSourceFolder = new PathString(Path.GetRelativePath(repository.Path, docsetPath));
            return ToDocfxConfig(repository.Branch ?? "master", opsConfig, buildSourceFolder);
        }

        private static (string xrefEndpoint, string[] xrefQueryTags, JObject config) ToDocfxConfig(string branch, OpsConfig opsConfig, PathString buildSourceFolder)
        {
            var result = new JObject();
            var dependencies = GetDependencies(opsConfig, branch, buildSourceFolder);

            result["dependencies"] = new JObject(
                from dep in dependencies
                where !dep.name.Equals("_themes", StringComparison.OrdinalIgnoreCase) &&
                      !dep.name.Equals("_themes.pdf", StringComparison.OrdinalIgnoreCase) &&
                      !dep.name.Equals("_repo.en-us", StringComparison.OrdinalIgnoreCase)
                select new JProperty(dep.path, dep.obj));

            result["template"] = dependencies.FirstOrDefault(
                dep => dep.name.Equals("_themes", StringComparison.OrdinalIgnoreCase)).obj;

            result["output"] = new JObject { ["pdf"] = opsConfig.NeedGeneratePdfUrlTemplate };

            result["contribution"] = new JObject
            {
                ["repositoryUrl"] = opsConfig.GitRepositoryUrlOpenToPublicContributors,
                ["repositoryBranch"] = opsConfig.GitRepositoryBranchOpenToPublicContributors,
            };

            var docsetConfig = opsConfig.DocsetsToPublish.FirstOrDefault(
                config => config.BuildSourceFolder.FolderEquals(buildSourceFolder));

            if (docsetConfig != null)
            {
                if (!string.IsNullOrEmpty(docsetConfig.DocsetName))
                {
                    result["name"] = docsetConfig.DocsetName;
                    result["extend"] = OpsConfigAdapter.BuildConfigApi;
                }

                result["globalMetadata"] = new JObject
                {
                    ["open_to_public_contributors"] = docsetConfig.OpenToPublicContributors,
                };
            }

            return (opsConfig.XrefEndpoint, docsetConfig?.XrefQueryTags ?? Array.Empty<string>(), result);
        }

        private static (JObject obj, string path, string name)[] GetDependencies(OpsConfig config, string branch, string buildSourceFolder)
        {
            return (
                from dep in config.DependentRepositories
                let path = Path.GetRelativePath(buildSourceFolder, dep.PathToRoot)
                let depBranch = dep.BranchMapping.TryGetValue(branch, out var mappedBranch) ? mappedBranch : dep.Branch
                let obj = new JObject
                {
                    ["url"] = dep.Url,
                    ["includeInBuild"] = dep.IncludeInBuild,
                    ["branch"] = depBranch,
                }
                select (obj, path, dep.PathToRoot)).ToArray();
        }
    }
}
