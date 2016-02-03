﻿// ----------------------------------------------------------------------------------
//
// Copyright Microsoft Corporation
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StaticAnalysis.HelpAnalyzer
{
    /// <summary>
    /// Static analyzer for PowerShell Help
    /// </summary>
    public class HelpAnalyzer : IStaticAnalyzer
    {
        public HelpAnalyzer()
        {
            Name = "Help Analyzer";
        }
        public AnalysisLogger Logger { get; set; }
        public string Name { get; private set; }

        private AppDomain _appDomain;

        /// <summary>
        /// Given a set of directory paths containing PowerShell module folders, analyze the help 
        /// in the module folders and report any issues
        /// </summary>
        /// <param name="scopes"></param>
        public void Analyze(IEnumerable<string> scopes)
        {
            var savedDirectory = Directory.GetCurrentDirectory();
            var processedHelpFiles = new List<string>();
            var helpLogger = Logger.CreateLogger<HelpIssue>("HelpIssues.csv");
            foreach (var baseDirectory in scopes.Where(s => Directory.Exists(Path.GetFullPath(s))))
            {
                foreach (var directory in Directory.EnumerateDirectories(Path.GetFullPath(baseDirectory)))
                {
                    var helpFiles = Directory.EnumerateFiles(directory, "*.dll-Help.xml")
                        .Where(f => !processedHelpFiles.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)).ToList();
                    if (helpFiles.Any())
                    {
                        Directory.SetCurrentDirectory(directory);
                        foreach (var helpFile in helpFiles)
                        {
                           var cmdletFile = helpFile.Substring(0, helpFile.Length - "-Help.xml".Length);
                            var helpFileName = Path.GetFileName(helpFile);
                            var cmdletFileName = Path.GetFileName(cmdletFile);
                            if (File.Exists(cmdletFile) )
                            {
                                processedHelpFiles.Add(helpFileName);
                                helpLogger.Decorator.AddDecorator((h) =>
                                {
                                    h.HelpFile = helpFileName;
                                    h.Assembly = cmdletFileName;
                                }, "Cmdlet");
                                var proxy = AppDomainHelpers.CreateProxy<CmdletLoader>(directory, out _appDomain);
                                var cmdlets = proxy.GetCmdlets(cmdletFile);
                                var helpRecords = CmdletHelpParser.GetHelpTopics(helpFile, helpLogger);
                                ValidateHelpRecords(cmdlets, helpRecords, helpLogger);
                                helpLogger.Decorator.Remove("Cmdlet");
                                AppDomain.Unload(_appDomain);
                            }
                        }

                        Directory.SetCurrentDirectory(savedDirectory);
                    }
                }
            }
        }

        private void ValidateHelpRecords(IList<CmdletHelpMetadata> cmdlets, IList<string> helpRecords, 
            ReportLogger<HelpIssue> helpLogger)
        {
            foreach (var cmdlet in cmdlets)
            {
                if (!helpRecords.Contains(cmdlet.CmdletName, StringComparer.OrdinalIgnoreCase))
                {
                    helpLogger.LogRecord(new HelpIssue
                    {
                        Target = cmdlet.ClassName,
                        Severity = 1,
                        Description = string.Format("Help missing for cmdlet {0} implemented by class {1}", 
                        cmdlet.CmdletName, cmdlet.ClassName),
                        Remediation = string.Format("Add Help record for cmdlet {0} to help file.", cmdlet.CmdletName)
                    });
                }
            }
        }
    }
}
