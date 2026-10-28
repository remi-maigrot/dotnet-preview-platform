using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;

namespace Build.Tasks;

[TaskName("Docker-Build")]
[TaskDescription("Builds the container images, tagged with the commit SHA (pushes them with --push=true)")]
public sealed class DockerBuildTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        foreach (var image in context.Images)
        {
            var arguments = new ProcessArgumentBuilder()
                .Append("buildx build")
                .AppendSwitchQuoted("--file", image.Dockerfile)
                .AppendSwitchQuoted("--tag", context.ImageReference(image))
                .AppendSwitchQuoted("--build-arg", $"VERSION={context.Version}")
                .AppendSwitchQuoted("--build-arg", $"REVISION={context.GitSha}")
                .AppendSwitchQuoted("--label", $"org.opencontainers.image.created={DateTimeOffset.UtcNow:O}");

            if (!string.IsNullOrEmpty(context.Platforms))
            {
                arguments.AppendSwitch("--platform", context.Platforms);
            }

            // A multi-platform image cannot be loaded into the local engine: it is either pushed or only cached.
            arguments.Append(context.Push ? "--push --provenance=mode=max --sbom=true" : "--load");
            context.Run("docker", arguments.Append("."));
        }
    }
}
