# unicode-aware-string-reverse

A correctness-first, allocation-conscious string reversal for .NET — handles surrogate pairs and combining marks without corrupting them.

## Why not just `s.Reverse()` or `Array.Reverse`?

.NET strings are UTF-16 under the hood, and a `System.Char` is one UTF-16 *code unit* — not necessarily one visual character. Reversing a string char-by-char silently corrupts two common cases:

- **Surrogate pairs** — astral-plane characters (most emoji, many historic and CJK Extension B characters) are represented as two `char` values. Reverse them naively and you get two unpaired, invalid surrogates instead of one character.
- **Combining character sequences** — an accented letter can be a base character followed by one or more combining marks (`e` + `´` instead of a single precomposed `é`). Reverse naively and the accent ends up attached to a completely different letter.

```csharp
"café".Reverse();          // naive: "éfac" broken into garbage if é is decomposed
"👍🏽".Reverse();            // naive: corrupts the emoji + skin-tone modifier pairing
```

This library reverses by *grapheme cluster* — surrogate pairs and base+mark sequences are treated as a single, indivisible unit — while still being fast for the overwhelming common case of plain ASCII or simple non-combining text.

## Usage

```csharp
using Shared.Services;

string? result = "hello".Reverse();       // "olleh"
string? result2 = "👍🏽".Reverse();         // emoji stays intact
string? result3 = ((string?)null).Reverse(); // null (garbage-in/garbage-out)
```

`Reverse` is an extension method on `string?`:

| Input | Result |
|---|---|
| `null` | `null` |
| `""` | `""` (same reference) |
| single UTF-16 code unit | unchanged (same reference) |
| well-formed text | new reversed string |
| starts with an orphan combining mark | throws `ArgumentException` |

See [Error handling](#error-handling) for why the last case throws instead of silently producing a non-invertible result.

## How it works

`Reverse` dispatches to one of three tiers, cheapest first, so well-formed real-world text (which is overwhelmingly ASCII or simple non-combining Unicode) never pays for cluster analysis it doesn't need:

| Tier | Trigger | Strategy |
|---|---|---|
| 1 | Pure 7-bit ASCII | `System.Text.Ascii.IsValid` (SIMD-accelerated) confirms the fast path is safe, then a direct UTF-16 code-unit swap written straight into the result via `string.Create` |
| 2 | Non-ASCII, but no surrogate pairs or combining marks (plain Cyrillic, Greek, most CJK, etc.) | Same direct code-unit swap as Tier 1 — safe because every code unit is its own grapheme cluster |
| 3 | Contains surrogate pairs and/or combining marks | Grapheme-cluster boundaries computed in one linear pass into a pooled `int[]` (`ArrayPool<int>`), then copied directly into the result in reverse cluster order via `string.Create` |

No tier allocates more than the single resulting string itself — no `List<string>`, no per-cluster string allocations, no intermediate `char[]` copied a second time. Benchmarked allocation is flat (equal to the output string's own size) across all three tiers at every input length from empty strings up to 1M+ characters.

## Error handling

Grapheme-cluster reversal attaches trailing combining marks to whichever base character precedes them. Because of how the clustering scan works, an "orphan" mark with no base to attach to can only ever occur at index 0 of the string — everywhere else, a run of marks is always absorbed by the cluster started by the preceding base character. If reversal proceeded anyway, that orphan mark could end up attached to a *different* base character after reversal, silently producing a result that wouldn't reverse back to the original input.

Rather than silently produce a non-invertible result, `Reverse` throws `ArgumentException` when the input begins with a combining mark and is longer than one character (a single-character string has nothing to reorder, so it's exempt). This is a deliberate contract: **`Reverse` guarantees round-trip correctness (`s == s.Reverse().Reverse()`) for well-formed text**, and fails loudly rather than quietly for the one input shape where that guarantee can't hold.

## Project structure

```
Shared.Services/                 Production library (StringExtensions.Reverse and friends)
Shared.Services.Test/            xUnit tests: edge cases + randomized stress/fuzz tests
Shared.Services.Benchmark/       BenchmarkDotNet suites
```

Test and benchmark data generators share the same boundary constants and Unicode-category helpers as the production code (via `internal` + `InternalsVisibleTo`), so synthetic test data can't silently drift out of sync with the algorithm it's exercising.

## Testing

```
dotnet test
```

Covers:
- End cases: `null`, empty, single-character, whitespace-only, malformed lone surrogate half
- ASCII, plain BMP Unicode, combining-mark attachment, surrogate pairs, and combinations of the above
- The orphan-leading-combining-mark contract (throws, with the correct `ParamName`), including single-character exemption
- Randomized stress tests: 500 trials each of random ASCII and random well-formed Unicode (mixing ASCII, BMP base + 0-2 combining marks, and surrogate-pair base + 0-2 combining marks) asserting double-reversal is the identity, plus a variant that deliberately prepends an orphan mark on ~10% of trials and asserts the throw instead
- Very long strings (up to 1,000,000 characters) for both ASCII and mixed Unicode content

## Benchmarking

Two benchmark classes, both run via `dotnet run -c Release` from `Shared.Services.Benchmark` (a `BenchmarkSwitcher` discovers all benchmark classes in the assembly):

```
dotnet run -c Release                                    # interactive picker
dotnet run -c Release -- --filter *StringReverseBenchmarks*
dotnet run -c Release -- --filter *RealTextReverseBenchmarks*
```

- **`StringReverseBenchmarks`** — synthetic data (pure ASCII, plain BMP Unicode, combining marks, surrogate pairs) at lengths from 0 to 1,048,576 code units, with `[MemoryDiagnoser]` to track allocation per tier.
- **`RealTextReverseBenchmarks`** — reverses text extracted from real files (plain text in UTF-8/UTF-16/Windows-1252, `.docx` paragraph text, `.xlsx` cell text) rather than synthetic data, to validate performance against realistic clustering patterns — long ASCII runs interrupted by occasional diacritics or symbols, rather than a uniform per-character random mix. Edit the file paths in `RealTextReverseBenchmarks.Samples()` before running. True Windows-1252 decoding requires the `System.Text.Encoding.CodePages` package plus registering `CodePagesEncodingProvider` once at startup.

Benchmark results (raw `.csv`/`.md`/`.html`) are written automatically to `BenchmarkDotNet.Artifacts/results/` next to the executable.

## Known limitations / scope

- **Well-formed input is assumed** beyond the specific orphan-leading-mark guard. Unpaired surrogates *elsewhere* in a string are handled gracefully (treated as their own single-unit cluster, no crash, no round-trip break) but a string is not validated as well-formed UTF-16 in general.
- **Raw-byte fuzz testing** (reinterpreting arbitrary binary data directly as UTF-16 code units to hunt for crashes on adversarial input) was deliberately scoped out of this pass and left for a future effort.

## Requirements

- .NET 10
- xUnit (test project)
- BenchmarkDotNet (benchmark project)

