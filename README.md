# media-encoding

Automatic DVD, Blu-Ray, and UltraHD Blu-Ray ripping tool with intelligent metadata lookup and file organization.

## Quick Start

```bash
# 1) Install MakeMKV, FFmpeg, and .NET SDK 10+ (see Requirements)

# 2) View help
dotnet run --project src/MediaEncoding -- --help

# 3) Set API keys (optional but recommended)
export TMDB_API_KEY="your_key_here"
export OMDB_API_KEY="your_key_here"

# 4) Rip a movie
dotnet run --project src/MediaEncoding -- --output ~/Movies --mode movie

# 5) Rip a TV season
dotnet run --project src/MediaEncoding -- --output ~/TV --mode tv --title "Breaking Bad" --season 1
```

## Basic Usage

### Movie Ripping

```bash
dotnet run --project src/MediaEncoding -- --output ~/Movies --mode movie --title "The Matrix" --year 1999
```

This will:
1. Scan the disc (default: `disc:0`)
2. Identify the main feature (45+ minutes)
3. Rip with highest resolution, all English audio/subtitles
4. Save as `~/Movies/The Matrix (1999).mkv`

### TV Series Ripping

```bash
dotnet run --project src/MediaEncoding -- --output ~/TV --mode tv --title "Breaking Bad" --season 1
```

This will:
1. Scan the disc
2. Identify all episodes (20-50 minutes each)
3. Rip each episode with English audio/subtitles
4. Save as `~/TV/Breaking Bad - S01E01.mkv`, `~/TV/Breaking Bad - S01E02.mkv`, etc.

## Command-Line Options

| Option | Value | Default | Description |
|--------|-------|---------|-------------|
| `--output` | `PATH` | *required* | Output directory for ripped files |
| `--mode` | `movie\|tv` | *required* | Content type (movie for films, tv for series) |
| `--disc` | `disc:N\|/dev/...` | `disc:0` | Optical drive path |
| `--temp` | `PATH` | `{output}/.makemkv` | Temporary ripping directory |
| `--title` | `TEXT` | *(disc title)* | Custom title for file naming |
| `--year` | `YYYY` | *(from metadata)* | Release year (movies only) |
| `--season` | `N` | `1` | Season number (TV only) |
| `--episode-start` | `N` | `1` | Starting episode number (TV only) |
| `--disc-type` | `dvd\|bd\|uhd` | *auto-detect* | Override disc type for size estimation |
| `--debug` | *(flag)* | false | Enable debug logging |
| `-h, --help` | *(flag)* | false | Show help message and exit |

## Configuration

### Environment Variables (Recommended)

```bash
export TMDB_API_KEY="your_tmdb_api_key"      # Primary metadata source
export OMDB_API_KEY="your_omdb_api_key"      # Fallback metadata source
```

To make permanent, add to `~/.bashrc`, `~/.zshrc`, or equivalent:

```bash
# ~/.bashrc or ~/.zshrc
export TMDB_API_KEY="your_tmdb_api_key"
export OMDB_API_KEY="your_omdb_api_key"
```

### Config File (Alternative)

Edit [src/MediaEncoding/appsettings.yaml](src/MediaEncoding/appsettings.yaml):

```yaml
metadata:
  lookup_enabled: true
  omdb_api_key: "your_key"
  tmdb_api_key: "your_key"
```

**Note:** Environment variables override config file values.

## Requirements

### Software Dependencies

1. **MakeMKV** - Disc ripping
   - https://www.makemkv.com/

2. **FFmpeg** - Media processing
   ```bash
   # Ubuntu/Debian
   sudo apt-get install ffmpeg
   
   # macOS
   brew install ffmpeg
   
   # Fedora
   sudo dnf install ffmpeg
   ```

3. **.NET SDK 10.0+** - Runtime
   ```bash
   # Check version
   dotnet --version
   ```
   
   Get from: https://dotnet.microsoft.com/download

4. **API Keys** (Optional but recommended)
   - **TMDB**: https://www.themoviedb.org/settings/api (free)
   - **OMDB**: https://www.omdbapi.com/apikey.aspx (free tier available)

### Hardware Requirements

- DVD or Blu-Ray drive
- 5-100 GB free disk space (depending on disc type)
- Adequate processing power

## Installation

```bash
git clone https://github.com/mapitman/media-encoding.git
cd media-encoding
dotnet restore src/MediaEncoding
dotnet build src/MediaEncoding
```

Then verify it works:
```bash
dotnet run --project src/MediaEncoding -- --output /tmp/test --mode movie
```

*The app requires `--output` and `--mode` at minimum. See **Command-Line Options** above.*

## How It Works

### Workflow

The application:

1. **Scans disc** - Uses `makemkvcon` to identify all titles and their properties
2. **Identifies content** - Finds the main feature (movies) or episodes (TV series)
3. **Looks up metadata** - Queries OMDB then TMDB for official titles and years
4. **Rips titles** - Extracts using MakeMKV at highest available quality
5. **Selects tracks** - Includes English audio and subtitles only
6. **Renames & saves** - Moves to output directory with proper naming

### File Naming

**Movies:** `Title (Year).mkv`  
Example: `The Matrix (1999).mkv`

**TV Series:** `Show Name - S##E##.mkv`  
Example: `Breaking Bad - S01E01.mkv`, `Breaking Bad - S01E02.mkv`

### Track Selection

- **Video:** Highest resolution stream (copied, not re-encoded)
- **Audio:** All English stereo (2ch) and surround (5.1+) tracks
- **Subtitles:** All English subtitle tracks

## Troubleshooting

### "Missing required tools" error

Ensure all dependencies are installed and in PATH:
```bash
which makemkvcon ffmpeg ffprobe
```

### Disc not detected

1. Insert disc and wait for it to be recognized
2. Check if readable: `makemkvcon info disc:0`
3. Try alternate device: `--disc /dev/sr0` or `/dev/sr1`

### Insufficient disk space

- DVD: 4-8 GB
- Blu-Ray: 15-35 GB
- UHD 4K: 40-100 GB

Ensure both `--temp` and `--output` directories have sufficient space.

### MakeMKV beta key required

Visit: https://www.makemkv.com/forum/viewtopic.php?f=5&t=1053

### Permission denied

Some systems require elevated permissions to access optical drives:
```bash
sudo dotnet run --project src/MediaEncoding -- --output ~/Movies --mode movie
```

## Examples

See [EXAMPLES.md](EXAMPLES.md) for additional examples including:

```yaml
   metadata:
     lookup_enabled: true
     omdb_api_key: "your_omdb_api_key"
     tmdb_api_key: "your_tmdb_api_key"
```

## Output Formats

- **Container:** Matroska (MKV)
- **Video:** Copied without re-encoding (preserves original codec and quality)
- **Audio:** Copied without re-encoding (AC3, DTS, TrueHD, etc.)
- **Subtitles:** Copied as-is (PGS, SRT, VobSub, etc.)

## Performance

Typical times (varies by drive speed and disc condition):

| Type | Size | Time |
|------|------|------|
| DVD | 4-8 GB | 10-30 min |
| Blu-Ray | 15-35 GB | 30-90 min |
| UHD 4K | 40-100 GB | 60-180 min |

## License

Personal use only. Ensure you own the physical media and comply with applicable copyright laws.

## Contributing

Contributions welcome! Please submit issues or pull requests.

## Support

- Check [Troubleshooting](#troubleshooting) above
- Open an issue on [GitHub](https://github.com/mapitman/media-encoding/issues)
- Visit [MakeMKV forums](https://www.makemkv.com/forum/)

## Acknowledgments

- **MakeMKV** - Disc ripping engine
- **FFmpeg** - Media processing
