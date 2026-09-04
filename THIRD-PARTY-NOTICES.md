# Third-party and trademark notices

Last reviewed: 2026-09-05

This is an independent, unofficial accessibility and language-learning overlay. It is not endorsed by, sponsored by, or affiliated with Blizzard Entertainment. Diablo, Diablo Immortal, and Blizzard Entertainment are trademarks or registered trademarks of Blizzard Entertainment, Inc. The download does not contain game binaries, artwork, audio, story text, fonts, or other Blizzard assets.

The public-facing application name is **Game English Coach**. References to Diablo Immortal describe compatibility and the user's selected game window.

## Distributed libraries

| Component | Version | Attribution | License | Project |
|---|---:|---|---|---|
| EdgeTTS.DotNet | 0.4.0 | Curry / contributors | MIT | https://github.com/twn39/EdgeTTS.DotNet |
| NAudio.Core / NAudio.WinMM | 2.2.1 | Copyright Mark Heath / contributors | MIT | https://github.com/naudio/NAudio |
| Vosk API | 0.3.38 | Copyright Alpha Cephei Inc / contributors | Apache-2.0 | https://github.com/alphacep/vosk-api |
| Microsoft .NET runtime | 10.x | Copyright .NET Foundation and contributors | MIT and bundled third-party notices | https://github.com/dotnet/runtime |

NuGet package metadata is the version source for the library licenses above. A self-contained release may also include platform/runtime components listed in Microsoft's accompanying notices.

## Downloaded tools and models

These are downloaded during setup rather than committed to this repository:

| Component | License information | Source |
|---|---|---|
| Ollama | MIT | https://github.com/ollama/ollama/blob/main/LICENSE |
| Qwen 3.5 0.8B and 2B models | Apache-2.0 as shown in each Ollama model manifest | https://ollama.com/library/qwen3.5 |
| `vosk-model-small-en-us-0.15` | Apache-2.0 | https://alphacephei.com/vosk/models |

Each component remains governed by its own license. The setup script verifies the Vosk archive SHA-256 and the Windows signature on the downloaded Ollama installer; a valid signature does not replace the user's review of upstream terms.

Canonical license texts used by these components are included in `licenses/MIT.txt` and `licenses/Apache-2.0.txt`. Component-specific copyright and NOTICE files, when supplied by an upstream package, remain controlling.

## Online services and reference data

- Edge natural voice and optional Azure translation are Microsoft network services and remain subject to Microsoft's terms.
- The local build cache extracts a small set of factual build fields from a linked Dexerto page at runtime and does not redistribute the article. Dexerto retains rights in its page and editorial content.

Blizzard's published trademark guidelines restrict commercial use and prohibit implying sponsorship. Keep this project non-commercial unless separate permission and legal review are obtained: https://www.blizzard.com/en-us/legal/38fd0408-8431-469a-99bc-2cd9eb9462c8/blizzard-entertainment-trademark-usage-guidelines

## Project license status

No license for this repository's original source code has been selected by the copyright owner yet. Public GitHub visibility and downloadable releases allow inspection and ordinary use of the release, but do not by themselves grant permission to modify or redistribute the source. The owner should add an explicit project license before accepting outside redistribution or contributions.

This notice is a practical engineering audit, not legal advice or an absolute guarantee against every claim. It should be reviewed again whenever a dependency, model, data source, product name, or distribution method changes.
