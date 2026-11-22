# Non-graphical regression. Links the exact production timing/path code in a disposable .NET project.
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path (Join-Path $PSScriptRoot '../scripts/video')).Path
$temp = (Resolve-Path ([System.IO.Path]::GetTempPath())).Path.TrimEnd([char[]]'\/')
$work = Join-Path $temp ('du-video-capture-test-' + [Guid]::NewGuid().ToString('N'))
$work = (New-Item -ItemType Directory -Path $work).FullName
$links = @()
try {
    $escaped = [System.Security.SecurityElement]::Escape($source)
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net9.0</TargetFramework><OutputType>Exe</OutputType><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><RollForward>LatestMajor</RollForward></PropertyGroup>
  <ItemGroup><Compile Include="$escaped/VideoCaptureTiming.cs;$escaped/VideoCaptureFolders.cs" /></ItemGroup>
</Project>
"@ | Set-Content -Encoding UTF8 (Join-Path $work 'Test.csproj')
    @'
using DynamiteUniverse.Video;
using static DynamiteUniverse.Video.VideoCaptureTiming;

static void Equal(int expected, int actual, string name)
{
    if (expected != actual) throw new Exception($"{name}: expected {expected}, got {actual}");
}
static void Reject(Action act, string name)
{
    try { act(); }
    catch (InvalidDataException) { return; }
    throw new Exception($"{name}: expected rejection");
}

Equal(210, AvailablePreviewFrames(3, 3.5, 7), "normal interval");
Equal(2, AvailablePreviewFrames(1, 0.025, 1.025), "tie at decoded EOF");
Equal(1, AvailablePreviewFrames(1, 0.025, 1.02499), "genuinely on lower side of tie");
Equal(0, AvailablePreviewFrames(1, 0.01, 1.004), "valid sub-half-frame EOF interval");
Equal(1, AvailablePreviewFrames(0, .5 / 60, 1), "half-frame from authored duration");
Equal(3600, AvailablePreviewFrames(0, 80, 100), "60 s upper bound");
Reject(() => AvailablePreviewFrames(-0.1, 3, 8), "negative start");
Reject(() => AvailablePreviewFrames(3, 1, 3), "start at EOF");
Reject(() => AvailablePreviewFrames(1.99999999, 1, 2), "start rounds to EOF in Godot float seek");
Equal(209, FramesUntilPreviewEnd(91, 210, 92), "first observation is after Play frame");
Equal(0, FramesUntilPreviewEnd(91, 1, 92), "one-frame audition stops immediately at next frame");
Reject(() => FramesUntilPreviewEnd(91, 1, 93), "late stop must not extend audition");
Equal(500, EndFrame(500, 0), "zero additional hold includes reveal frame");
Equal(741, EndFrame(EndFrame(500, 240), 1), "four-second extra hold with inclusive terminal draw");

var root = args[0];
var scope = Path.Combine(root, "paths");
var user = Path.Combine(scope, "profile", "user");
var output = Path.Combine(scope, "output");
VideoCaptureFolders.Validate(user, output);
Reject(() => VideoCaptureFolders.Validate(user, Path.Combine(user, "charts")), "nested output");
// The same common junction is legitimate; a junction in only one branch is not.
var mounted = Path.Combine(root, "mount");
VideoCaptureFolders.Validate(Path.Combine(mounted, "profile", "user"), Path.Combine(mounted, "output"));
var aliased = Path.Combine(scope, "alias", "user");
Reject(() => VideoCaptureFolders.Validate(user, Path.Combine(aliased, "charts")), "cross-branch junction aliases the profile");
Console.WriteLine("PASS capture timing and Windows junction isolation");
'@ | Set-Content -Encoding UTF8 (Join-Path $work 'Program.cs')
    $scope = Join-Path $work 'paths'
    $profile = Join-Path $scope 'profile'
    New-Item -ItemType Directory -Force -Path (Join-Path $profile 'user/charts/snapshot'), (Join-Path $scope 'output') | Out-Null
    $links += (New-Item -ItemType Junction -Path (Join-Path $work 'mount') -Target $scope).FullName
    $links += (New-Item -ItemType Junction -Path (Join-Path $scope 'alias') -Target $profile).FullName
    dotnet run --project (Join-Path $work 'Test.csproj') -p:NuGetAudit=false -- $work
    if ($LASTEXITCODE -ne 0) { throw "Capture regression failed: exit $LASTEXITCODE" }
} finally {
    if ((Split-Path -Parent $work) -ne $temp -or (Split-Path -Leaf $work) -notmatch '^du-video-capture-test-[0-9a-f]{32}$') {
        throw 'Refusing to delete a path outside the verified temporary test root.'
    }
    foreach($link in $links) { [System.IO.Directory]::Delete($link) }
    Remove-Item -LiteralPath $work -Recurse -Force
}
