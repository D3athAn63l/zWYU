// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace zWYU.Tests
{
    /// <summary>
    /// Guards the promises made in the PR description that are checkable without the game:
    /// upstream licensing and attribution, a separate identity, correct About metadata, no shipped binaries,
    /// translation keys that match the code, and "no IL transpilers".
    /// </summary>
    public class RepositoryInvariantTests
    {
        static readonly string Root = FindRoot();

        static string FindRoot() {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !(File.Exists(Path.Combine(dir.FullName, "NOTICE")) && Directory.Exists(Path.Combine(dir.FullName, "About"))))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("repository root (NOTICE + About/) not found above " + AppContext.BaseDirectory);
        }

        static string Read(string relative) => File.ReadAllText(Path.Combine(Root, relative));

        static IEnumerable<string> SourceFiles() =>
            Directory.EnumerateFiles(Path.Combine(Root, "Source", "zWYU"), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

        // ---- licensing & attribution ---------------------------------------------------------------------------------------

        [Fact]
        public void License_IsTheFullAgplV3_LikeTheUpstreamProjects() {
            var license = Read("LICENSE");
            Assert.Contains("GNU AFFERO GENERAL PUBLIC LICENSE", license);
            Assert.Contains("Version 3, 19 November 2007", license);
            Assert.Contains("END OF TERMS AND CONDITIONS", license);
        }

        [Fact]
        public void Notice_CreditsTheOriginalAuthorAndBothUpstreamRepositories_AndSaysThisIsADerivedWork() {
            var notice = Read("NOTICE");
            Assert.Contains("Christopher S. Galpin", notice);
            Assert.Contains("https://github.com/CodeOptimist/rimworld-while-youre-up", notice);
            Assert.Contains("https://github.com/CodeOptimist/rimworld-codeoptimist-library", notice);
            Assert.Contains("DERIVED WORK", notice);
            Assert.Contains("GNU Affero General Public License", notice);
            Assert.Contains("PublishedFileId", notice); // says it is deliberately not reused
        }

        [Fact]
        public void EverySourceFile_CarriesTheLicenseIdentifier() {
            var missing = SourceFiles().Where(f => !File.ReadLines(f).Take(3).Any(l => l.Contains("SPDX-License-Identifier: AGPL-3.0-or-later"))).ToList();
            Assert.True(missing.Count == 0, "missing SPDX header: " + string.Join(", ", missing.Select(f => Path.GetRelativePath(Root, f))));
        }

        [Fact]
        public void CodeAdaptedFromTheLibrary_NamesItsOrigin() {
            foreach (var file in new[] { "Source/zWYU/Lib/CodeOptimist/Gui.cs", "Source/zWYU/Lib/CodeOptimist/Extensions.cs" }) {
                var text = Read(file);
                Assert.Contains("Christopher S. Galpin", text);
                Assert.Contains("rimworld-codeoptimist-library", text);
                Assert.Contains("Modified for zWYU", text);
            }
        }

        // ---- identity & metadata -------------------------------------------------------------------------------------------

        [Fact]
        public void AboutXml_IsASeparateProjectTargetingOnly16() {
            var about = XDocument.Parse(Read("About/About.xml")).Root;
            Assert.Equal("ModMetaData", about.Name.LocalName);
            var packageId = about.Element("packageId").Value;
            Assert.Equal("D3athAn63l.zWYU", packageId);
            Assert.DoesNotContain("CodeOptimist", packageId);
            Assert.Equal(new[] { "1.6" }, about.Element("supportedVersions").Elements("li").Select(e => e.Value).ToArray());
            Assert.Contains(about.Element("modDependencies").Elements("li"), li => li.Element("packageId").Value == "brrainz.harmony");
            Assert.Contains(about.Element("incompatibleWith").Elements("li"), li => li.Value == "CodeOptimist.JobsOfOpportunity");
            var description = about.Element("description").Value;
            Assert.Contains("Christopher S. Galpin", description);
            Assert.Contains("Affero", description);
            Assert.Contains("https://github.com/D3athAn63l/zWYU", description);
        }

        [Fact]
        public void TheOriginalSteamPublishedFileId_IsNotReused() {
            var found = Directory.EnumerateFiles(Root, "PublishedFileId.txt", SearchOption.AllDirectories).Where(f => !f.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar)).ToList();
            Assert.Empty(found);
            const string originalId = "2034960453";
            var offenders = Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                            && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !f.EndsWith("RepositoryInvariantTests.cs") && !f.EndsWith(".dll") && !f.EndsWith(".pdb"))
                .Where(f => { try { return File.ReadAllText(f).Contains(originalId); } catch { return false; } })
                .ToList();
            Assert.Empty(offenders);
        }

        // ---- no game binaries ----------------------------------------------------------------------------------------------

        [Fact]
        public void GitIgnore_KeepsBuildOutputAndGameBinariesOut() {
            var ignore = Read(".gitignore");
            Assert.Contains("1.6/Assemblies/*", ignore);
            Assert.Contains("Assembly-CSharp", ignore);
            Assert.Contains("0Harmony.dll", ignore);
            Assert.Contains("**/bin/", ignore);
            Assert.Contains("**/obj/", ignore);
        }

        [Fact]
        public void NoBinariesOrMachineSpecificPaths_AreTracked() {
            var tracked = GitLsFiles();
            if (tracked == null) return; // not a git checkout (e.g. source archive): the .gitignore test above still applies

            var binaries = tracked.Where(f => Regex.IsMatch(f, @"\.(dll|exe|pdb|so|dylib|zip)$", RegexOptions.IgnoreCase)).ToList();
            Assert.True(binaries.Count == 0, "tracked binaries: " + string.Join(", ", binaries));

            var absolutePath = new Regex(@"(?i)(?:[A-Z]:\\(?:Program Files|Users|Steam)|/home/[a-z0-9_.-]+/|/Users/[A-Za-z0-9_.-]+/|steamapps[\\/]common)");
            var offenders = tracked
                .Where(f => Regex.IsMatch(f, @"\.(cs|csproj|xml|md|yml|yaml|props|sln|json|txt)$") && !f.EndsWith("RepositoryInvariantTests.cs") && f != "LICENSE")
                .Where(f => absolutePath.IsMatch(File.ReadAllText(Path.Combine(Root, f))))
                .ToList();
            Assert.True(offenders.Count == 0, "machine-specific absolute paths in: " + string.Join(", ", offenders));
        }

        static List<string> GitLsFiles() {
            try {
                var psi = new ProcessStartInfo("git", "ls-files") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                using var process = Process.Start(psi);
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0 ? output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList() : null;
            } catch {
                return null;
            }
        }

        // ---- job-system safety: no IL manipulation -------------------------------------------------------------------------

        [Fact]
        public void TheModUsesNoHarmonyTranspilers() {
            var offenders = SourceFiles()
                .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\[HarmonyTranspiler\]|IEnumerable<CodeInstruction>|\bTranspiler\s*\(|OpCodes\."))
                .Select(f => Path.GetRelativePath(Root, f)).ToList();
            Assert.True(offenders.Count == 0, "IL transpiler code found in: " + string.Join(", ", offenders));
        }

        [Fact]
        public void EveryHarmonyPatchClass_IsRegisteredWithAFeatureInTheBootstrap() {
            var bootstrap = Read("Source/zWYU/Patches/PatchBootstrap.cs");
            var patchClasses = SourceFiles()
                .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\[HarmonyPatch[^\]]*\]\s*internal static class (\w+)").Cast<Match>().Select(m => m.Groups[1].Value))
                .ToList();
            Assert.NotEmpty(patchClasses);
            foreach (var cls in patchClasses)
                Assert.True(bootstrap.Contains("typeof(" + cls + ")"), $"{cls} is a Harmony patch class but PatchBootstrap never applies it (harmony.PatchAll() is deliberately not used)");

            // ...and nothing calls PatchAll (it aborts the whole mod on the first patch that cannot apply)
            var calls = SourceFiles().Where(f => File.ReadLines(f).Any(l => !l.TrimStart().StartsWith("//") && l.Contains(".PatchAll("))).ToList();
            Assert.True(calls.Count == 0, "PatchAll() is called in: " + string.Join(", ", calls));
        }

        // ---- translations --------------------------------------------------------------------------------------------------

        static HashSet<string> KeyedKeys() {
            var keys = XDocument.Parse(Read("Languages/English/Keyed/zWYU.xml")).Root.Elements().Select(e => e.Name.LocalName).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count()); // no duplicate keys
            Assert.All(keys, k => Assert.StartsWith("zWYU_", k));
            return keys.Select(k => k.Substring("zWYU_".Length)).ToHashSet();
        }

        [Fact]
        public void EveryTranslationKeyUsedByTheCode_ExistsInTheKeyedFile() {
            var keys = KeyedKeys();
            var code = string.Join("\n", SourceFiles().Select(File.ReadAllText));

            var literalKeys = Regex.Matches(code, "\"([A-Za-z0-9_]+)\"\\.ModTranslate").Cast<Match>().Select(m => m.Groups[1].Value)
                .Concat(new[] { "Opportunity_LoadReport", "HaulBeforeCarry_LoadReport" }) // chosen by a ?: in the report patch
                .Distinct().ToList();
            Assert.Contains("Opportunity_Tab", literalKeys);
            foreach (var key in literalKeys)
                Assert.True(keys.Contains(key), "missing translation key zWYU_" + key);

            // every setting drawn by a Draw* helper needs a label (tooltips are optional)
            var drawn = Regex.Matches(code, @"Draw(?:Bool|Float|Percent|Int|Enum)\([^;]*?nameof\(settings\.(\w+)\)").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.True(drawn.Count >= 15, "expected the settings UI to draw many settings, found " + drawn.Count);
            foreach (var name in drawn)
                Assert.True(keys.Contains("Label_" + name), "missing label zWYU_Label_" + name);
        }

        [Fact]
        public void EnumSettings_HaveALabelForEveryValue() {
            var keys = KeyedKeys();
            foreach (var value in new[] { "Vanilla", "Default", "Pathfinding" }) Assert.Contains("Label_Opportunity_PathChecker_" + value, keys);
            foreach (var value in new[] { "Off", "Summary", "Verbose" }) Assert.Contains("Label_DebugLevel_" + value, keys);
        }
    }
}
