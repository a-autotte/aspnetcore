﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.DotNet.Cli.CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Extensions.ApiDescription.Tool.Commands
{
    internal class InvokeCommand : HelpCommandBase
    {
        private const string InsideManName = "GetDocument.Insider";

        private readonly ProjectOptions _projectOptions = new ProjectOptions();
        private IList<string> _args;

        public override void Configure(CommandLineApplication command)
        {
            base.Configure(command);

            _projectOptions.Configure(command);
            _args = command.RemainingArguments;
        }

        protected override void Validate()
        {
            base.Validate();
            _projectOptions.Validate();
        }

        protected override int Execute()
        {
            var thisPath = Path.GetFullPath(Path.GetDirectoryName(typeof(InvokeCommand).Assembly.Location));

            var projectName = _projectOptions.ProjectName.Value();
            var assemblyPath = _projectOptions.AssemblyPath.Value();
            var targetDirectory = Path.GetDirectoryName(assemblyPath);

            string executable = null;
            var cleanupExecutable = false;
            try
            {
                string toolsDirectory;
                var args = new List<string>();
                var targetFramework = new FrameworkName(_projectOptions.TargetFramework.Value());
                switch (targetFramework.Identifier)
                {
                    case ".NETFramework":
                        cleanupExecutable = true;
                        toolsDirectory = Path.Combine(
                            thisPath,
                            _projectOptions.Platform.Value() == "x86" ? "net461-x86" : "net461");

                        var executableSource = Path.Combine(toolsDirectory, InsideManName + ".exe");
                        executable = Path.Combine(targetDirectory, InsideManName + ".exe");
                        File.Copy(executableSource, executable, overwrite: true);

                        var configPath = assemblyPath + ".config";
                        if (File.Exists(configPath))
                        {
                            File.Copy(configPath, executable + ".config", overwrite: true);
                        }
                        break;

                    case ".NETCoreApp":
                        if (targetFramework.Version < new Version(2, 1))
                        {
                            throw new CommandException(Resources.FormatOldNETCoreAppProject(
                                projectName,
                                targetFramework.Version));
                        }

                        executable = "dotnet";
                        toolsDirectory = Path.Combine(thisPath, "netcoreapp2.1");

                        args.Add("exec");
                        args.Add("--depsFile");
                        args.Add(Path.ChangeExtension(assemblyPath, ".deps.json"));

                        var projectAssetsFile = _projectOptions.AssetsFile.Value();
                        if (!string.IsNullOrEmpty(projectAssetsFile) && File.Exists(projectAssetsFile))
                        {
                            using var reader = new JsonTextReader(File.OpenText(projectAssetsFile));
                            var projectAssets = JToken.ReadFrom(reader);
                            var packageFolders = projectAssets["packageFolders"]
                                .Children<JProperty>()
                                .Select(p => p.Name);

                            foreach (var packageFolder in packageFolders)
                            {
                                args.Add("--additionalProbingPath");
                                args.Add(packageFolder.TrimEnd(Path.DirectorySeparatorChar));
                            }
                        }

                        var runtimeConfigPath = Path.ChangeExtension(assemblyPath, ".runtimeconfig.json");
                        if (File.Exists(runtimeConfigPath))
                        {
                            args.Add("--runtimeConfig");
                            args.Add(runtimeConfigPath);
                        }
                        else
                        {
                            var runtimeFrameworkVersion = _projectOptions.RuntimeFrameworkVersion.Value();
                            if (!string.IsNullOrEmpty(runtimeFrameworkVersion))
                            {
                                args.Add("--fx-version");
                                args.Add(runtimeFrameworkVersion);
                            }
                        }

                        args.Add(Path.Combine(toolsDirectory, InsideManName + ".dll"));
                        break;

                    case ".NETStandard":
                        throw new CommandException(Resources.FormatNETStandardProject(projectName));

                    default:
                        throw new CommandException(
                            Resources.FormatUnsupportedFramework(projectName, targetFramework.Identifier));
                }

                args.AddRange(_args);
                args.Add("--assembly");
                args.Add(assemblyPath);
                args.Add("--project");
                args.Add(projectName);
                args.Add("--tools-directory");
                args.Add(toolsDirectory);

                if (Reporter.IsVerbose)
                {
                    args.Add("--verbose");
                }

                if (Reporter.NoColor)
                {
                    args.Add("--no-color");
                }

                if (Reporter.PrefixOutput)
                {
                    args.Add("--prefix-output");
                }

                return Exe.Run(executable, args);
            }
            finally
            {
                if (cleanupExecutable && !string.IsNullOrEmpty(executable))
                {
                    // Ignore errors about in-use files. Should still be marked for delete after process cleanup.
                    try
                    {
                        File.Delete(executable);
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }

                    try
                    {
                        File.Delete(executable + ".config");
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
        }
    }
}
