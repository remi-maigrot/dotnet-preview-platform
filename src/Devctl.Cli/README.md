# devctl

Developer CLI of [dotnet-preview-platform](https://github.com/remi-maigrot/dotnet-preview-platform).

```bash
devctl doctor                       # check Docker, k3d, kubectl and helm
devctl up                           # local cluster, platform and the API of your working copy
devctl preview create --pr 42       # an isolated environment for pull request 42
devctl preview list
devctl status
devctl logs api --follow
devctl down
```

Every command accepts `--verbose` and `--output json`. Exit codes: 0 success, 1 failure, 2 usage error,
3 missing prerequisite, 4 not found.
