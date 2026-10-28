# devctl

Developer CLI of [dotnet-preview-platform](https://github.com/remi-maigrot/dotnet-preview-platform).

## Install

From a clone of the repository, pack it with the build and install it as a global tool:

```bash
dotnet run --project build -- --target Package
dotnet tool install --global Devctl --add-source artifacts/packages --prerelease
```

Without installing, `dotnet run --project src/Devctl.Cli -- <command>` does the same from the repository root.
Each release also attaches the package to its GitHub Release.

## Commands

```bash
devctl doctor                       # check Docker, k3d, kubectl and helm
devctl up                           # local cluster, platform and the API of your working copy
devctl preview create --pr 42       # an isolated environment for pull request 42
devctl preview list
devctl preview diagnose --pr 42     # why a preview is not working: findings, failing pods, warnings, logs
devctl status
devctl logs api --follow
devctl down
```

Every command accepts `--verbose` and `--output json`. Exit codes: 0 success, 1 failure, 2 usage error,
3 missing prerequisite, 4 not found.

`devctl preview diagnose` works offline. To have its report explained by a language model, point it at any
OpenAI-compatible chat completions endpoint:

```bash
export DEVCTL_LLM_ENDPOINT=https://llm.example/v1   # a hosted service, a gateway, or a local server
export DEVCTL_LLM_MODEL=<model name>
export DEVCTL_LLM_API_KEY=...                       # if the endpoint needs one
devctl preview diagnose --pr 42 --show-payload      # what would be sent, redacted, without sending it
```

Passwords, connection strings, tokens, keys and email addresses are removed before anything is sent.
