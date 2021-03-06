#tool "nuget:?package=coveralls.io&version=1.4.2"
#tool "nuget:?package=GitVersion.CommandLine&version=5.1.3"
#tool "nuget:?package=gitlink&version=3.1.0"
#tool "nuget:?package=GitReleaseNotes&version=0.7.1"
#tool "nuget:?package=NUnit.ConsoleRunner&version=3.10.0"

#addin "nuget:?package=Cake.Git&version=0.21.0"
#addin "nuget:?package=Nuget.Core&version=2.14.0"
#addin "nuget:?package=Cake.Coveralls&version=0.10.1"
#addin "nuget:?package=Cake.Coverlet&version=2.3.4"

using NuGet;

var target = Argument("target", "Default");
var artifactsDir = "./artifacts/";
var solutionPath = "../BSN.Commons.sln";
var projectName = "BSN.Commons";
var mainProject = "../Source/BSN.Commons/BSN.Commons.csproj";
var presentationProject = "../Source/BSN.Commons.PresentationInfrastructure/BSN.Commons.PresentationInfrastructure.csproj";
var testFolder = "../Test/BSN.Commons.Tests/";
var testProjectDll = testFolder + "bin/Release/net472/BSN.Commons.Tests.dll";
var testProject = testFolder + "BSN.Commons.Tests.csproj";
var coverageResultsFileName = "coverage.xml";
var testResultsFileName = "nunitResults.xml";
var currentBranch = Argument<string>("currentBranch", GitBranchCurrent("../").FriendlyName);
var isReleaseBuild = string.Equals(currentBranch, "master", StringComparison.OrdinalIgnoreCase);
var configuration = "Release";
var nugetApiKey = Argument<string>("nugetApiKey", null);
var coverallsToken = Argument<string>("coverallsToken", null);
var nugetSource = "https://api.nuget.org/v3/index.json";
var repoUrl = "https://github.com/BSVN/Commons.git";
var projectUrl = "https://github.com/BSVN/Commons";

Task("Clean")
    .Does(() => {
        if (DirectoryExists(artifactsDir))
        {
            DeleteDirectory(
                artifactsDir, 
                new DeleteDirectorySettings {
                    Recursive = true,
                    Force = true
                }
            );
        }
        CreateDirectory(artifactsDir);
        DotNetCoreClean(solutionPath);
});


Task("Restore")
    .Does(() => {
        NuGetRestore(solutionPath);
});

GitVersion versionInfo = null;
Task("Version")
    .Does(() => {

        GitVersion(new GitVersionSettings{
            UpdateAssemblyInfo = true,
            OutputType = GitVersionOutput.BuildServer
        });

        versionInfo = GitVersion(new GitVersionSettings{
            OutputType = GitVersionOutput.Json
        });

        UpdateVersion(mainProject);
        UpdateVersion(presentationProject);
});

Task("Build")
    .IsDependentOn("Restore")
    .IsDependentOn("Clean")
    .Does(() => {
        DotNetCoreBuild(
            solutionPath,
            new DotNetCoreBuildSettings 
            {
                Configuration = configuration
            }
        );
});

Task("Test")
    .IsDependentOn("Build")
    .Does(() => {
        var settings = new DotNetCoreTestSettings {
        };

        var coverletSettings = new CoverletSettings {
            CollectCoverage = true,
            CoverletOutputFormat = CoverletOutputFormat.opencover,
            CoverletOutputDirectory = Directory(@"./coverage-test/"),
            CoverletOutputName = coverageResultsFileName
        };
 
        DotNetCoreTest(testProject, settings, coverletSettings);
        MoveFile("./coverage-test/" + coverageResultsFileName, artifactsDir + coverageResultsFileName);

        NUnit3(testProjectDll, new NUnit3Settings()
        {
            Results = new [] {new NUnit3Result() { FileName = artifactsDir + testResultsFileName } },
        });

        if (AppVeyor.IsRunningOnAppVeyor)
            AppVeyor.UploadTestResults(artifactsDir + testResultsFileName, AppVeyorTestResultsType.NUnit3);
});

Task("UploadCoverage")
    .IsDependentOn("Test")
    .Does(() =>
    {
        CoverallsIo(artifactsDir + coverageResultsFileName, new CoverallsIoSettings()
        {
            RepoToken = coverallsToken
        });
});

Task("Package")
    .IsDependentOn("Version")
    .Does(() => {
        var settings = new DotNetCorePackSettings
        {
            OutputDirectory = artifactsDir,
            NoBuild = true,
            Configuration = configuration
        };

        //GenerateReleaseNotes();

        DotNetCorePack(mainProject, settings);
        DotNetCorePack(presentationProject, settings);
/*
        System.IO.File.WriteAllLines(artifactsDir, new[]{
            "nuget:" + projectName + "." + versionInfo.NuGetVersion + ".nupkg",
            "nugetSymbols:" + projectName + "." + versionInfo.NuGetVersion + ".symbols.nupkg",
            "releaseNotes:releasenotes.md"
        });
        */

        if (AppVeyor.IsRunningOnAppVeyor)
        {
            foreach (var file in GetFiles(artifactsDir + "**/*"))
                AppVeyor.UploadArtifact(file.FullPath);
        }
});

Task("Publish")
    .IsDependentOn("Package")
    .Does(() => {
        var pushSettings = new DotNetCoreNuGetPushSettings 
        {
            Source = nugetSource,
            ApiKey = nugetApiKey
        };

        var pkgs = GetFiles(artifactsDir + "*.nupkg");
        foreach(var pkg in pkgs) 
        {
            if(!IsNuGetPublished(pkg)) 
            {
                Information($"Publishing \"{pkg}\".");
                DotNetCoreNuGetPush(pkg.FullPath, pushSettings);
            }
            else {
                Information($"Bypassing publishing \"{pkg}\" as it is already published.");
            }
            
        }
});

private bool IsNuGetPublished(FilePath packagePath) {
    var package = new ZipPackage(packagePath.FullPath);

    var latestPublishedVersions = NuGetList(
        package.Id,
        new NuGetListSettings 
        {
            Prerelease = true
        }
    );

    return latestPublishedVersions.Any(p => package.Version.Equals(new SemanticVersion(p.Version)));
}

private void GenerateReleaseNotes()
{
    GitReleaseNotes(artifactsDir + "/releasenotes.md", new GitReleaseNotesSettings {
        WorkingDirectory         = artifactsDir,
        Verbose                  = true,
        IssueTracker             = GitReleaseNotesIssueTracker.GitHub,
        AllTags                  = true,
        RepoUrl                  = repoUrl,
        RepoBranch               = "master",
        IssueTrackerUrl          = projectUrl,
        Version                  = versionInfo.NuGetVersion,
        AllLabels                = true
    });

    if (string.IsNullOrEmpty(System.IO.File.ReadAllText("./artifacts/releasenotes.md")))
        System.IO.File.WriteAllText("./artifacts/releasenotes.md", "No issues closed since last release");
}

private void UpdateVersion(string projectPath)
{
    Information("UpdateVersion .................................................");
    Information(projectPath);
    // Update projectPath.json
    string pureVersion = XmlPeek(projectPath, "//Version");
    Information(pureVersion);
    string assemblyVersion = XmlPeek(projectPath, "//AssemblyVersion");
    string fileVersion = XmlPeek(projectPath, "//FileVersion");

    var updatedProjectJson = System.IO.File.ReadAllText(projectPath)
        .Replace(pureVersion, versionInfo.NuGetVersion)
        .Replace(fileVersion, versionInfo.NuGetVersion)
        .Replace(assemblyVersion, versionInfo.NuGetVersion);

    System.IO.File.WriteAllText(projectPath, updatedProjectJson);
}

Task("BuildAndTest")
    .IsDependentOn("Build")
    .IsDependentOn("Test");

Task("CompleteWithoutPublish")
    .IsDependentOn("Build")
    .IsDependentOn("Test")
    .IsDependentOn("UploadCoverage");

if(isReleaseBuild)
{
    Information("Release build");
    Task("Complete")
        .IsDependentOn("Build")
        .IsDependentOn("Test")
        .IsDependentOn("UploadCoverage")
        .IsDependentOn("Publish");
}
else
{
    Information("Development build");
    Task("Complete")
        .IsDependentOn("Build")
        .IsDependentOn("Test")
        .IsDependentOn("UploadCoverage");
}

Task("Default")
    .IsDependentOn("Build")
	.IsDependentOn("Test")
    .IsDependentOn("Package");


RunTarget(target);