using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

using pyRevitLabs.TargetApps.Revit;

namespace pyRevitLabs.UnitTests.RevitAddons {
    [TestClass()]
    public class RevitAddonsTests {
        [TestMethod()]
        public void GetRevitAddonsFolder_UserLevel_AllVersions_Test() {
            // Test that user-level path is unchanged for all versions
            var path2025 = RevitAddons.GetRevitAddonsFolder(2025, allUsers: false);
            var path2027 = RevitAddons.GetRevitAddonsFolder(2027, allUsers: false);
            var path2030 = RevitAddons.GetRevitAddonsFolder(2030, allUsers: false);

            // All should use AppData
            var expectedBasePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            
            Assert.IsTrue(path2025.StartsWith(expectedBasePath), 
                $"User-level path for 2025 should start with {expectedBasePath}");
            Assert.IsTrue(path2027.StartsWith(expectedBasePath), 
                $"User-level path for 2027 should start with {expectedBasePath}");
            Assert.IsTrue(path2030.StartsWith(expectedBasePath), 
                $"User-level path for 2030 should start with {expectedBasePath}");

            // Verify path structure
            Assert.IsTrue(path2025.Contains(@"Autodesk\Revit\Addins\2025") || 
                          path2025.Contains("Autodesk/Revit/Addins/2025"),
                $"Path 2025 should contain correct structure: {path2025}");
            Assert.IsTrue(path2027.Contains(@"Autodesk\Revit\Addins\2027") || 
                          path2027.Contains("Autodesk/Revit/Addins/2027"),
                $"Path 2027 should contain correct structure: {path2027}");
        }

        [TestMethod()]
        public void GetRevitAddonsFolder_AllUsers_Pre2027_Test() {
            // Test that all-users path uses ProgramData for Revit ≤2026
            var path2024 = RevitAddons.GetRevitAddonsFolder(2024, allUsers: true);
            var path2025 = RevitAddons.GetRevitAddonsFolder(2025, allUsers: true);
            var path2026 = RevitAddons.GetRevitAddonsFolder(2026, allUsers: true);

            // All should use CommonApplicationData (ProgramData)
            var expectedBasePath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            
            Assert.IsTrue(path2024.StartsWith(expectedBasePath), 
                $"All-users path for 2024 should start with {expectedBasePath}");
            Assert.IsTrue(path2025.StartsWith(expectedBasePath), 
                $"All-users path for 2025 should start with {expectedBasePath}");
            Assert.IsTrue(path2026.StartsWith(expectedBasePath), 
                $"All-users path for 2026 should start with {expectedBasePath}");

            // Verify path structure
            Assert.IsTrue(path2026.Contains(@"Autodesk\Revit\Addins\2026") || 
                          path2026.Contains("Autodesk/Revit/Addins/2026"),
                $"Path 2026 should contain correct structure: {path2026}");
        }

        [TestMethod()]
        public void GetRevitAddonsFolder_AllUsers_Post2027_Test() {
            // Test that all-users path uses Program Files for Revit ≥2027
            var path2027 = RevitAddons.GetRevitAddonsFolder(2027, allUsers: true);
            var path2028 = RevitAddons.GetRevitAddonsFolder(2028, allUsers: true);
            var path2030 = RevitAddons.GetRevitAddonsFolder(2030, allUsers: true);

            // All should use Program Files
            var expectedBasePath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            
            Assert.IsTrue(path2027.StartsWith(expectedBasePath), 
                $"All-users path for 2027 should start with {expectedBasePath}, got: {path2027}");
            Assert.IsTrue(path2028.StartsWith(expectedBasePath), 
                $"All-users path for 2028 should start with {expectedBasePath}, got: {path2028}");
            Assert.IsTrue(path2030.StartsWith(expectedBasePath), 
                $"All-users path for 2030 should start with {expectedBasePath}, got: {path2030}");

            // Verify path structure - should contain "Revit <year>\Addins\<year>"
            Assert.IsTrue(path2027.Contains(@"Revit 2027\Addins\2027") || 
                          path2027.Contains("Revit 2027/Addins/2027"),
                $"Path 2027 should contain correct structure: {path2027}");
            Assert.IsTrue(path2028.Contains(@"Revit 2028\Addins\2028") || 
                          path2028.Contains("Revit 2028/Addins/2028"),
                $"Path 2028 should contain correct structure: {path2028}");
        }

        [TestMethod()]
        public void GetRevitAddonsFolder_AllUsers_2027Boundary_Test() {
            // Test the exact boundary at year 2027
            var path2026 = RevitAddons.GetRevitAddonsFolder(2026, allUsers: true);
            var path2027 = RevitAddons.GetRevitAddonsFolder(2027, allUsers: true);

            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            // 2026 should use ProgramData
            Assert.IsTrue(path2026.StartsWith(programData), 
                $"2026 should use ProgramData: {path2026}");

            // 2027 should use Program Files
            Assert.IsTrue(path2027.StartsWith(programFiles), 
                $"2027 should use Program Files: {path2027}");
        }

        [TestMethod()]
        public void GetRevitAddonsFilePath_Consistency_Test() {
            // Test that GetRevitAddonsFilePath uses GetRevitAddonsFolder correctly
            // This is an integration test to ensure the methods work together
            
            // For user-level
            var userFilePath2027 = RevitAddons.GetRevitAddonsFilePath(2027, "TestAddin", allusers: false);
            var userFolder2027 = RevitAddons.GetRevitAddonsFolder(2027, allUsers: false);
            Assert.IsTrue(userFilePath2027.StartsWith(userFolder2027),
                $"User file path should be within user folder: {userFilePath2027} vs {userFolder2027}");

            // For all-users (non-elevated, so it should still use user path)
            var allUsersFilePath2027 = RevitAddons.GetRevitAddonsFilePath(2027, "TestAddin", allusers: false);
            Assert.IsTrue(allUsersFilePath2027.Contains("TestAddin.addin"),
                $"File path should contain addin extension: {allUsersFilePath2027}");
        }
    }
}
