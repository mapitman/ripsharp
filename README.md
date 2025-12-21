# media-encoding

Tools and scripts for ripping, encoding and organizing media files from DVDs, Blu-Ray, and Blu-Ray UltraHD discs.

## Features

- **Automatic disc scanning** - Detects and analyzes all titles on the disc
- **Smart content detection** - Identifies main movie or TV episodes automatically
- **Optimal quality** - Rips at the highest available resolution
- **Audio track selection** - Includes both stereo and surround sound audio when available
- **Subtitle support** - Automatically includes English subtitles if present
- **Metadata lookup** - Attempts to identify and properly name media files
- **Batch processing** - Can process entire TV season discs at once

## Requirements

### Software Dependencies

1. **MakeMKV** - For disc ripping
   - Download from: https://www.makemkv.com/
   - Ubuntu/Debian: Follow instructions on MakeMKV website
   - macOS: Install via DMG from website

2. **FFmpeg** - For video analysis and re-encoding
   ```bash
   # Ubuntu/Debian
   sudo apt-get install ffmpeg
   
   # macOS
   brew install ffmpeg
   
   # Fedora
   sudo dnf install ffmpeg
   ```

3. **Python 3.7+** - For running the main script
   ```bash
   # Ubuntu/Debian
   sudo apt-get install python3 python3-pip
   
   # macOS (usually pre-installed)
   brew install python3
   ```

4. **Python dependencies** - PyYAML for configuration file support
   ```bash
   pip install -r requirements.txt
   ```

### Hardware Requirements

- DVD or Blu-Ray drive
- Sufficient disk space (movies can be 5-50GB each)
- Adequate processing power for video encoding

## Installation

1. Clone this repository:
   ```bash
   git clone https://github.com/mapitman/media-encoding.git
   cd media-encoding
   ```

2. Install Python dependencies:
   ```bash
   pip install -r requirements.txt
   ```

3. Make scripts executable:
   ```bash
   chmod +x rip_disc.py rip_movie.sh rip_tv.sh
   ```

4. Verify dependencies are installed:
   ```bash
   ./rip_disc.py --help
   ```

## Usage

### Ripping a Movie

Use the `rip_movie.sh` script for movie discs:

```bash
./rip_movie.sh --title "Movie Title" --year 2024 --output /path/to/output
```

**Options:**
- `--disc DISC` - Disc path (default: `disc:0` for first drive)
- `--output DIR` - Output directory (required)
- `--temp DIR` - Temporary directory (default: `/tmp/makemkv`)
- `--title TITLE` - Movie title for naming
- `--year YEAR` - Release year for naming

**Example:**
```bash
./rip_movie.sh --title "The Matrix" --year 1999 --output ~/Movies
```

This will:
1. Scan the disc and identify the main feature
2. Rip the main movie at highest resolution
3. Include English audio (stereo and surround)
4. Include English subtitles
5. Save as `~/Movies/The Matrix (1999).mkv`

### Ripping a TV Series

Use the `rip_tv.sh` script for TV series discs:

```bash
./rip_tv.sh --title "Show Name" --season 1 --output /path/to/output
```

**Options:**
- `--disc DISC` - Disc path (default: `disc:0`)
- `--output DIR` - Output directory (required)
- `--temp DIR` - Temporary directory (default: `/tmp/makemkv`)
- `--title TITLE` - TV series title for naming
- `--season NUM` - Season number (default: 1)

**Example:**
```bash
./rip_tv.sh --title "Breaking Bad" --season 1 --output ~/TV
```

This will:
1. Scan the disc and identify all episodes
2. Rip each episode at highest resolution
3. Include English audio and subtitles
4. Save as `~/TV/Breaking Bad - S01E01.mkv`, `~/TV/Breaking Bad - S01E02.mkv`, etc.

### Advanced Usage (Python Script)

For more control, use the Python script directly:

```bash
./rip_disc.py --output /path/to/output [OPTIONS]
```

**Options:**
- `--disc DISC` - Disc path (default: `disc:0`)
- `--output DIR` - Output directory (required)
- `--temp DIR` - Temporary directory
- `--tv` - Treat as TV series disc
- `--title TITLE` - Title for naming
- `--year YEAR` - Year for naming
- `--season NUM` - Season number for TV series
- `--debug` - Enable debug logging

**Examples:**

Rip a movie with custom disc path:
```bash
./rip_disc.py --disc /dev/sr0 --title "Inception" --year 2010 --output ~/Movies
```

Rip TV series episodes:
```bash
./rip_disc.py --disc disc:0 --tv --title "Friends" --season 1 --output ~/TV
```

### Batch Processing

For processing multiple discs in sequence, use the batch scripts:

**Batch Movie Ripping:**
```bash
# Edit batch_rip_movies.sh to add your movie list
nano batch_rip_movies.sh

# Then run the batch script
./batch_rip_movies.sh
```

**Batch TV Season Ripping:**
```bash
# Edit batch_rip_tv_season.sh to configure your series
nano batch_rip_tv_season.sh

# Then run the batch script
./batch_rip_tv_season.sh
```

The batch scripts will prompt you to insert each disc in sequence, making it easy to process your entire collection.

**More Examples:** See [EXAMPLES.md](EXAMPLES.md) for comprehensive usage examples including 4K UHD, multiple drives, network storage, and more.

## How It Works

### 1. Disc Scanning

The script uses `makemkvcon` to scan the disc and gather information about all titles, including:
- Duration
- Size
- Number of chapters
- Available audio and subtitle tracks

### 2. Content Identification

**For Movies:**
- Finds the longest title on the disc
- Must be at least 45 minutes long
- Typically this is the main feature film

**For TV Series:**
- Finds all titles between 20-50 minutes
- Sorts them in order
- Treats each as a separate episode

### 3. Track Selection

**Video:**
- Selects the highest resolution video stream
- Copies without re-encoding to preserve quality

**Audio:**
- Includes all English stereo (2 channel) tracks
- Includes all English surround sound (5.1+) tracks
- Copies without re-encoding to preserve quality

**Subtitles:**
- Includes all English subtitle tracks
- Preserves format (SRT, PGS, etc.)

### 4. File Naming

**Movies:** `Title (Year).mkv`
- Example: `The Matrix (1999).mkv`

**TV Series:** `Show Name - S##E##.mkv`
- Example: `Breaking Bad - S01E01.mkv`

## Configuration

### Configuration File

You can use a YAML configuration file to set default values and avoid passing them as command-line arguments each time:

1. Copy the example configuration:
   ```bash
   cp config.example.yaml config.yaml
   ```

2. Edit `config.yaml` with your preferred settings:
   ```yaml
   disc:
     default_path: "disc:0"
     default_temp_dir: "/tmp/makemkv"
   
   output:
     movies_dir: "~/Movies"
     tv_dir: "~/TV Shows"
   
   encoding:
     include_english_subtitles: true
     include_stereo_audio: true
     include_surround_audio: true
   ```

3. Use the configuration file with the `--config` option:
   ```bash
   ./rip_disc.py --config config.yaml --output ~/Movies --title "Movie Name"
   ```

Configuration values can be overridden by command-line arguments.

### Temporary Directory

By default, files are ripped to `/tmp/makemkv` before being processed. You can change this with the `--temp` option if you need more space:

```bash
./rip_movie.sh --temp /mnt/large-disk/temp --output ~/Movies
```

### Disc Detection

The script defaults to `disc:0` which is typically the first optical drive. If you have multiple drives or want to specify a device directly:

```bash
# Use second drive
./rip_movie.sh --disc disc:1 --output ~/Movies

# Use specific device (Linux)
./rip_disc.py --disc /dev/sr0 --output ~/Movies

# Use specific device (macOS)
./rip_disc.py --disc /dev/disk2 --output ~/Movies
```

## Troubleshooting

### "Missing required tools" error

Make sure MakeMKV and FFmpeg are installed and in your PATH:
```bash
which makemkvcon
which ffmpeg
which ffprobe
```

### MakeMKV beta key required

MakeMKV requires a license or beta key. Get a free beta key from:
https://www.makemkv.com/forum/viewtopic.php?f=5&t=1053

### Disc not detected

1. Verify the disc is inserted and readable
2. Check disc path with: `makemkvcon info disc:0`
3. Try specifying the device directly: `--disc /dev/sr0`

### Insufficient disk space

Blu-Ray discs can be very large (25-50GB). Make sure you have enough space in both the temp and output directories.

### Permission denied errors

The script may need elevated permissions to access optical drives:
```bash
sudo ./rip_movie.sh --title "Movie" --output ~/Movies
```

## Advanced Features

### Metadata Lookup (Future Enhancement)

The script includes a placeholder for online metadata lookup. To implement:

1. Sign up for a TMDB API key: https://www.themoviedb.org/settings/api
2. Modify the `lookup_metadata()` function in `rip_disc.py`
3. Install required Python packages: `pip install tmdbsimple`

This will enable automatic fetching of plot summaries, cast information, and accurate titles/years.

### Custom Track Selection

To modify which audio/subtitle tracks are included, edit the `encode_file()` function in `rip_disc.py`. Current logic:

- **Audio**: Include all English stereo (2ch) and surround (6+ch) tracks
- **Subtitles**: Include all English subtitle tracks

### Batch Processing

To process multiple discs, create a simple loop:

```bash
#!/bin/bash
for disc in "Movie1:1999" "Movie2:2000" "Movie3:2001"; do
    title="${disc%:*}"
    year="${disc#*:}"
    echo "Insert disc for $title and press Enter..."
    read
    ./rip_movie.sh --title "$title" --year "$year" --output ~/Movies
    echo "Eject disc and press Enter to continue..."
    read
done
```

## File Formats

### Input

- DVD Video (VIDEO_TS)
- Blu-Ray (BDMV)
- UltraHD Blu-Ray (4K)

### Output

All output files are in MKV (Matroska) format:
- Container: MKV
- Video: Original codec (H.264, H.265, VC-1, etc.) - copied without re-encoding
- Audio: Original codec (AC3, DTS, TrueHD, etc.) - copied without re-encoding
- Subtitles: Original format (PGS, SRT, VobSub) - copied

## Performance

### Typical Processing Times

- **DVD ripping**: 10-30 minutes
- **Blu-Ray ripping**: 30-90 minutes
- **UltraHD Blu-Ray**: 60-180 minutes

Times vary based on:
- Disc read speed
- Disc size
- CPU performance (though copying is fast)
- Disk I/O speed

### Disk Space Requirements

- **DVD**: 4-8 GB per movie
- **Blu-Ray**: 15-35 GB per movie
- **UltraHD Blu-Ray**: 40-100 GB per movie

## License

This project is provided as-is for personal use. Please ensure you own the physical media you are ripping and comply with your local copyright laws.

## Contributing

Contributions are welcome! Please feel free to submit issues or pull requests.

## Support

For issues or questions:
1. Check the Troubleshooting section above
2. Open an issue on GitHub
3. Consult the MakeMKV forums: https://www.makemkv.com/forum/

## Acknowledgments

- **MakeMKV** - For the excellent disc ripping functionality
- **FFmpeg** - For comprehensive media processing capabilities
