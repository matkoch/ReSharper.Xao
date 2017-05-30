﻿using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Navigation;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Caches;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.Util;

namespace ReSharper.Xao
{
    [RelatedFilesProvider(typeof(KnownProjectFileType))]
    public class ViewModelRelatedFilesProvider : IRelatedFilesProvider
    {
        private static readonly string[] ViewSuffixes = { "View", "Flyout", "UserControl", "Page" };

        private static readonly string[] TestSuffixes = { "Test", "Tests" };

        public IEnumerable<Tuple<IProjectFile, string, IProjectFile>> GetRelatedFiles(IProjectFile projectFile)
        {
            var typeNamesInFile = GetTypeNamesDefinedInFile(projectFile).ToList();

            var candidateTypeNames =
                GetMvvmTypeCandidates(typeNamesInFile)
                .Concat(GetTestTypeCandidates(typeNamesInFile));

            // Look for the candidate types in the solution.
            var solution = projectFile.GetSolution();
            var candidateTypes = new List<IClrDeclaredElement>();
            foreach (var candidateTypeName in candidateTypeNames)
            {
                var types = FindType(solution, candidateTypeName);
                candidateTypes.AddRange(types);
            }

            // Get the source files for each of the candidate types.
            var sourceFiles = new List<IPsiSourceFile>();
            foreach (var type in candidateTypes)
            {
                var sourceFilesForCandidateType = type.GetSourceFiles();
                sourceFiles.AddRange(sourceFilesForCandidateType);
            }

            var elementCollector = new RecursiveElementCollector<ITypeDeclaration>();
            foreach (var psiSourceFile in sourceFiles)
                foreach (var file in psiSourceFile.EnumerateDominantPsiFiles())
                    elementCollector.ProcessElement(file);

            var elements = elementCollector.GetResults();
            var projectFiles = elements.Select(declaration => declaration.GetSourceFile().ToProjectFile());

            var thisProjectName = projectFile.GetProject()?.Name;

            var rval = new List<Tuple<IProjectFile, string, IProjectFile>>();
            foreach (var file in projectFiles.OfType<ProjectFileImpl>().Distinct(pf => pf.Location.FullPath))
            {
                // Remove all extensions (e.g.: .xaml.cs).
                var fn = file.Name;
                var dotPos = fn.IndexOf('.');
                if (dotPos != -1)
                {
                    fn = fn.Substring(0, dotPos);
                }

                var display = fn.EndsWith("ViewModel")
                    ? "ViewModel"
                    : ViewSuffixes.Any(fn.EndsWith)
                        ? "View"
                        : TestSuffixes.Any(fn.EndsWith) ? "Tests" : "Implementation";

                var projectName = file.GetProject()?.Name;

                if (projectName != null &&
                    !string.Equals(thisProjectName, projectName, StringComparison.OrdinalIgnoreCase))
                {
                    display += $" (in {projectName})";
                }

                var tuple = Tuple.Create((IProjectFile)file, display, projectFile);

                rval.Add(tuple);
            }

            return rval;
        }

        private IEnumerable<string> GetMvvmTypeCandidates(IEnumerable<string> typeNamesInFile)
        {
            var candidates = new List<string>();

            // For each type name in the file, create a list of candidates.
            foreach (var typeName in typeNamesInFile)
            {
                // If a view model...
                if (typeName.EndsWith("ViewModel", StringComparison.OrdinalIgnoreCase))
                {
                    // Remove ViewModel from end and add all the possible suffixes.
                    var baseName = typeName.Substring(0, typeName.Length - 9);
                    candidates.AddRange(ViewSuffixes.Select(suffix => baseName + suffix));

                    // Add base if it ends in one of the view suffixes.
                    if (ViewSuffixes.Any(suffix => baseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
                    {
                        candidates.Add(baseName);
                    }
                }

                foreach (var suffix in ViewSuffixes)
                {
                    if (typeName.EndsWith(suffix))
                    {
                        // Remove suffix and add ViewModel.
                        var baseName = typeName.Substring(0, typeName.Length - suffix.Length);
                        var candidate = baseName + "ViewModel";
                        candidates.Add(candidate);

                        // Just add ViewModel
                        candidate = typeName + "ViewModel";
                        candidates.Add(candidate);
                    }
                }
            }

            return candidates;
        }

        private IEnumerable<string> GetTestTypeCandidates(IEnumerable<string> typeNamesInFile)
        {
            var candidates = new List<string>();

            // For each type name in the file, create a list of candidates.
            foreach (var typeName in typeNamesInFile)
            {
                // If not a test....
                if (!TestSuffixes.Any(s => typeName.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
                {
                    // Add all the possible test suffixes.
                    var baseName = typeName;
                    candidates.AddRange(TestSuffixes.Select(suffix => baseName + suffix));
                }

                foreach (var suffix in TestSuffixes)
                {
                    if (typeName.EndsWith(suffix))
                    {
                        // Remove the test suffix.
                        var baseName = typeName.Substring(0, typeName.Length - suffix.Length);
                        var candidate = baseName;
                        candidates.Add(candidate);
                    }
                }
            }

            return candidates;
        }

        private IEnumerable<string> GetTypeNamesDefinedInFile(IProjectFile projectFile)
        {
            IPsiSourceFile psiSourceFile = projectFile.ToSourceFile();
            if (psiSourceFile == null)
                return EmptyList<string>.InstanceList;

            return psiSourceFile.GetPsiServices().Symbols.GetTypesAndNamespacesInFile(psiSourceFile)
                                .OfType<ITypeElement>()
                                .Select(element => element.ShortName);
        }

        private static List<IClrDeclaredElement> FindType(ISolution solution, string typeToFind)
        {
            ISymbolScope declarationsCache = solution.GetPsiServices().Symbols
                .GetSymbolScope(LibrarySymbolScope.FULL, false);

            List<IClrDeclaredElement> results = declarationsCache.GetElementsByShortName(typeToFind).ToList();
            return results;
        }
    }
}