# Graphiti.Net

A .NET 10 port of [Graphiti](https://github.com/getzep/graphiti), the temporal context graph library by Zep.

Status: bootstrap. The packages contain no ported code yet. The [product specification](docs/product-specs/spec.md) holds the roadmap and the MVP goal.

## Development

You need Docker. If the .NET SDK version in `global.json` is missing, `scripts/setup-env.sh` installs it.

```bash
scripts/setup-env.sh
```

```bash
scripts/check.sh --test
```

The devcontainer in `.devcontainer/` and Claude cloud sessions run the same scripts.
[AGENTS.md](AGENTS.md) is the map for contributors and coding agents.

## License

Apache-2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE).
Graphiti.Net is an independent project. Zep does not maintain or endorse it.
