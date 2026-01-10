# Examples

This document provides practical examples for using the media encoding scripts.

## Configuration File Examples

### Using Environment Variables for API Keys (Recommended)

Set the TMDB API key as an environment variable for better security:

```bash
# Linux/macOS - Add to ~/.bashrc or ~/.zshrc
export TMDB_API_KEY="your_tmdb_api_key_here"

# Then use the script normally
./rip_disc.py --output ~/Movies --title "inception"
# The script will search TMDB and properly name the file: Inception (2010).mkv
```

### Using a Configuration File with Online Metadata Lookup

Create a `config.yaml` file to set your preferences:

```yaml
disc:
  default_path: "disc:0"
  default_temp_dir: "/mnt/ssd/temp"

output:
  movies_dir: "~/Movies"
  tv_dir: "~/TV Shows"

encoding:
  include_english_subtitles: true
  include_stereo_audio: true
  include_surround_audio: true
  min_movie_duration_seconds: 2700
  min_episode_duration_seconds: 1200
  max_episode_duration_seconds: 3600

metadata:
  lookup_enabled: true
  # API key can be set here, but environment variable is preferred
  tmdb_api_key: ""
```

Then use it with your rips:

```bash
# Set API key via environment variable (recommended)
export TMDB_API_KEY="your_tmdb_api_key_here"

# Use configuration file with online metadata lookup
./rip_disc.py --config config.yaml --output ~/Movies --title "inception"
# The script will search TMDB and properly name the file: Inception (2010).mkv

# Configuration values can be overridden
./rip_disc.py --config config.yaml --output ~/Movies --temp /tmp/custom --title "avatar"
```

## Basic Examples

### Using Make Targets

```bash
# Open an activated virtualenv shell
make activate

# Rip a movie using Make (outputs to ~/Movies)
make rip-movie OUTPUT=~/Movies EXTRA_ARGS='--title "The Matrix" --year 1999'

# Rip a TV season disc using Make (outputs to ~/TV)
make rip-tv OUTPUT=~/TV EXTRA_ARGS='--title "Breaking Bad" --season 1'
```

### Rip a Single Movie

```bash
# Basic movie rip
./rip_movie.sh --title "The Matrix" --year 1999 --output ~/Movies

# Movie with custom disc path
./rip_movie.sh --disc /dev/sr0 --title "Inception" --year 2010 --output ~/Movies

# Movie with custom temp directory
./rip_movie.sh --title "The Dark Knight" --year 2008 \
    --output ~/Movies --temp /mnt/scratch/temp
```

### Rip a TV Series Disc

```bash
# Basic TV series rip (Season 1)
./rip_tv.sh --title "Breaking Bad" --season 1 --output ~/TV

# TV series Season 2
./rip_tv.sh --title "Breaking Bad" --season 2 --output ~/TV

# TV series with custom disc path
./rip_tv.sh --disc disc:1 --title "Friends" --season 1 --output ~/TV
```

## Advanced Examples

### Online Metadata Lookup

Using TMDB for automatic metadata retrieval:

```bash
# Set API key via environment variable first (recommended)
export TMDB_API_KEY="your_api_key_here"

# Movie with online lookup (approximation of title is fine)
./rip_disc.py --config config.yaml --output ~/Movies --title "dark knight"
# TMDB will find: The Dark Knight (2008).mkv

# TV series with online lookup
./rip_disc.py --config config.yaml --output ~/TV --tv --title "game of thrones" --season 1
# TMDB will find: Game of Thrones - S01E01.mkv, etc.

# If year is ambiguous, provide it for better matching
./rip_disc.py --config config.yaml --output ~/Movies --title "dune" --year 2021
# Ensures you get Dune (2021) not Dune (1984)

# Works without config file if environment variable is set
export TMDB_API_KEY="your_api_key_here"
./rip_disc.py --output ~/Movies --title "inception"
# Output: Inception (2010).mkv
```

**Benefits of online metadata lookup:**
- Correct capitalization and formatting
- Accurate release years
- Proper series names
- Genre information (stored in metadata)
- Plot summaries and ratings

**Security Note:** Using environment variables for API keys is more secure than storing them in config files, as environment variables won't be accidentally committed to version control.

### Using the Python Script Directly

```bash
# Movie with debug logging
./rip_disc.py --output ~/Movies \
    --title "Blade Runner" --year 1982 --debug

# TV series with all options
./rip_disc.py --disc /dev/sr0 \
    --output ~/TV --temp /tmp/rip \
    --tv --title "The Wire" --season 1 --debug

# Movie from second optical drive
./rip_disc.py --disc disc:1 \
    --title "Avatar" --year 2009 \
    --output /mnt/media/Movies
```

## Batch Processing Examples

### Multiple Movies

Edit `batch_rip_movies.sh` and add your movie list:

```bash
MOVIES=(
    "The Matrix:1999"
    "The Matrix Reloaded:2003"
    "The Matrix Revolutions:2003"
)
```

Then run:

```bash
./batch_rip_movies.sh
```

The script will prompt you to insert each disc in sequence.

### TV Series Complete Season

Edit `batch_rip_tv_season.sh`:

```bash
SERIES_TITLE="Breaking Bad"
SEASON_NUMBER=1
NUM_DISCS=3  # Number of discs in season 1
```

Then run:

```bash
./batch_rip_tv_season.sh
```

## Specific Scenarios

### 4K UltraHD Blu-Ray

The script automatically handles UltraHD discs:

```bash
./rip_movie.sh --title "Blade Runner 2049" --year 2017 \
    --output ~/Movies/4K
```

Note: UltraHD rips can be 40-100GB and take 1-3 hours.

### DVD Collection

For standard DVDs:

```bash
./rip_movie.sh --title "The Godfather" --year 1972 \
    --output ~/Movies/DVDs
```

DVDs are typically 4-8GB and take 10-30 minutes.

### Multiple Audio/Subtitle Languages

The script defaults to English tracks. To customize, edit `rip_disc.py` and modify the `encode_file()` method.

For example, to include Spanish audio:

```python
# Around line 470, change:
if language in ('eng', 'en', '') or not language:
# To:
if language in ('eng', 'en', 'spa', 'es', '') or not language:
```

### Anime / Foreign Films

For non-English content, you may want to modify the subtitle selection:

```bash
# Edit rip_disc.py to include all subtitle tracks
# or manually specify tracks using ffmpeg after initial rip
```

## Working with Multiple Drives

If you have multiple optical drives:

```bash
# Terminal 1: Rip from first drive
./rip_movie.sh --disc disc:0 --title "Movie A" --output ~/Movies

# Terminal 2: Simultaneously rip from second drive
./rip_movie.sh --disc disc:1 --title "Movie B" --output ~/Movies
```

Make sure to use different temporary directories:

```bash
# Terminal 1
./rip_movie.sh --disc disc:0 --temp /tmp/makemkv0 \
    --title "Movie A" --output ~/Movies

# Terminal 2
./rip_movie.sh --disc disc:1 --temp /tmp/makemkv1 \
    --title "Movie B" --output ~/Movies
```

## Network/NAS Storage

For output to network storage:

```bash
# Mount your NAS first
mount /mnt/nas

# Then rip to it
./rip_movie.sh --title "Movie" --year 2024 --output /mnt/nas/Movies

# Use local temp directory for better performance
./rip_movie.sh --title "Movie" --year 2024 \
    --output /mnt/nas/Movies --temp /tmp/makemkv
```

## Troubleshooting Examples

### Test Disc Detection

```bash
# Check if disc is readable
makemkvcon info disc:0

# Check multiple drives
makemkvcon info disc:0
makemkvcon info disc:1
```

### Verify Output

```bash
# Check video info
ffprobe "Movie (2024).mkv"

# List streams
ffmpeg -i "Movie (2024).mkv"

# Check file size
ls -lh "Movie (2024).mkv"
```

### Clean Up After Failed Rip

```bash
# Remove temporary files
rm -rf /tmp/makemkv/*

# Or if using custom temp dir
rm -rf /path/to/temp/*
```

## Integration Examples

### Plex Media Server

Output directly to Plex directories:

```bash
# Movies
./rip_movie.sh --title "Movie Name" --year 2024 \
    --output "/var/lib/plexmediaserver/Library/Movies"

# TV Shows
./rip_tv.sh --title "Show Name" --season 1 \
    --output "/var/lib/plexmediaserver/Library/TV Shows"
```

### Jellyfin

Similar structure for Jellyfin:

```bash
./rip_movie.sh --title "Movie Name" --year 2024 \
    --output "/var/lib/jellyfin/movies"

./rip_tv.sh --title "Show Name" --season 1 \
    --output "/var/lib/jellyfin/shows"
```

## Automation Examples

### Cron Job for Scheduled Processing

Not recommended for interactive disc insertion, but possible for ISO files:

```bash
# Add to crontab: daily at 2 AM
0 2 * * * /path/to/media-encoding/process_queue.sh
```

### Post-Processing Hook

Add to the end of `rip_disc.py` to trigger post-processing:

```python
# After successful rip
subprocess.run(['/usr/local/bin/notify.sh', 'Rip complete', title])
```

## Performance Examples

### Fast Ripping (SSD Temp Storage)

```bash
# Use SSD for temporary storage
./rip_movie.sh --title "Movie" --year 2024 \
    --output ~/Movies --temp /mnt/ssd/temp
```

### Conserve Space

The script already conserves space by:
- Copying streams without re-encoding (no quality loss)
- Only including selected audio/subtitle tracks
- Cleaning up temporary files after encoding

To further reduce size, you would need to transcode (not recommended for archival):

```bash
# After ripping, manually transcode if needed
ffmpeg -i "Movie (2024).mkv" -c:v libx265 -crf 23 \
    -c:a copy -c:s copy "Movie (2024) - Compressed.mkv"
```

## Quality Verification

### Check Ripped File Quality

```bash
# Get detailed stream info
ffprobe -v quiet -print_format json -show_streams "Movie.mkv" | jq .

# Check video resolution
ffprobe -v error -select_streams v:0 \
    -show_entries stream=width,height "Movie.mkv"

# Check audio channels
ffprobe -v error -select_streams a \
    -show_entries stream=channels,channel_layout "Movie.mkv"

# Check subtitles
ffprobe -v error -select_streams s \
    -show_entries stream=index,codec_name "Movie.mkv"
```

## Legal and Ethical Use

Remember:
- Only rip discs you own
- Respect copyright laws in your jurisdiction
- Personal backup use only
- Don't distribute ripped content

These tools are provided for legal personal use only.
