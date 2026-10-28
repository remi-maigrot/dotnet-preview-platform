using Cake.Frosting;

namespace Build.Tasks;

[TaskName("CI")]
[TaskDescription("The full pipeline run by GitHub Actions on every push and pull request")]
[IsDependentOn(typeof(FormatCheckTask))]
[IsDependentOn(typeof(TestTask))]
[IsDependentOn(typeof(CoverageBadgeTask))]
[IsDependentOn(typeof(HelmLintTask))]
[IsDependentOn(typeof(ObservabilityCheckTask))]
[IsDependentOn(typeof(ScanTask))]
[IsDependentOn(typeof(SbomTask))]
[IsDependentOn(typeof(PackageTask))]
[IsDependentOn(typeof(PublishTask))]
public sealed class CiTask : FrostingTask;

[TaskName("Default")]
[IsDependentOn(typeof(TestTask))]
public sealed class DefaultTask : FrostingTask;
