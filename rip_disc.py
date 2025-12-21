#!/usr/bin/env python3
"""
Media Disc Ripping and Encoding Script
Rips DVDs, Blu-Ray, and UltraHD discs using makemkvcon and re-encodes them with optimal settings.
Includes online disc and metadata identification support.
"""

import argparse
import csv
import json
import logging
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import List, Dict, Optional, Tuple
import time
import select
import signal
import fcntl
import termios
import struct

# Configure logging with color support
# Use period between seconds and milliseconds in timestamps
class ColoredFormatter(logging.Formatter):
    """Custom formatter that adds colors and emojis based on log level."""
    
    COLORS = {
        'DEBUG': '\033[36m',    # Cyan
        'INFO': '\033[32m',     # Green
        'WARNING': '\033[33m',  # Yellow
        'ERROR': '\033[31m',    # Red
        'CRITICAL': '\033[41m', # Red background
    }
    RESET = '\033[0m'
    
    def format(self, record):
        # Add emoji and color based on level
        emoji_map = {
            'DEBUG': '🔍',
            'INFO': '✓',
            'WARNING': '⚠️',
            'ERROR': '❌',
            'CRITICAL': '🔥',
        }
        emoji = emoji_map.get(record.levelname, '')
        color = self.COLORS.get(record.levelname, '')
        
        # Create colored level name with emoji
        colored_level = f"{color}{emoji} {record.levelname}{self.RESET}"
        
        # Format timestamp
        timestamp = self.formatTime(record, '%Y-%m-%d %H:%M:%S')
        ms = record.msecs
        
        # Build final message
        message = record.getMessage()
        return f"{timestamp}.{int(ms):03d} - {colored_level} - {message}"

# Set up colored logger
logging.basicConfig(
    level=logging.INFO,
    format='%(message)s',  # We handle formatting in the formatter class
    stream=sys.stdout,
)

# Replace the default formatter with our colored one
handler = logging.getLogger().handlers[0]
handler.setFormatter(ColoredFormatter())

logger = logging.getLogger(__name__)

try:
    import yaml
    YAML_AVAILABLE = True
except ImportError:
    YAML_AVAILABLE = False
    logger.warning("PyYAML not installed. Configuration file support disabled. "
                  "Install with: pip install pyyaml")

try:
    import requests
    REQUESTS_AVAILABLE = True
except ImportError:
    REQUESTS_AVAILABLE = False
    logger.warning("requests library not installed. Online disc identification disabled. "
                  "Install with: pip install requests")

try:
    import tmdbsimple as tmdb
    TMDB_AVAILABLE = True
except ImportError:
    TMDB_AVAILABLE = False
    logger.info("tmdbsimple not installed. TMDB metadata lookup disabled. "
                "Install with: pip install tmdbsimple")

try:
    from rich.progress import Progress, BarColumn, TextColumn, TimeRemainingColumn
    from rich.console import Console
    from rich.live import Live
    RICH_AVAILABLE = True
    logger.info("✓ Rich library loaded successfully")
except ImportError as e:
    RICH_AVAILABLE = False
    logger.warning(f"Rich not available: {e}")

try:
    from colorama import init as colorama_init, Fore, Style
    COLORAMA_AVAILABLE = True
    colorama_init()
except ImportError:
    COLORAMA_AVAILABLE = False

try:
    import discid
    DISCID_AVAILABLE = True
except ImportError:
    DISCID_AVAILABLE = False
    logger.debug("discid library not available. Disc ID calculation disabled. "
                 "Install with: pip install discid (requires libdiscid system library)")

# Constants
MIN_EPISODE_DURATION_SECONDS = 1200  # 20 minutes
MAX_EPISODE_DURATION_SECONDS = 3600  # 60 minutes
MIN_MOVIE_DURATION_SECONDS = 2700    # 45 minutes
MIN_EPISODE_CHAPTERS = 1              # Minimum chapters to consider as episode

# Regex pattern for sanitizing filenames
# Removes: < > : " / \ | ? * (Windows/Linux reserved chars)
# and control characters \x00-\x1f (ASCII 0-31)
FILENAME_SANITIZE_PATTERN = re.compile(r'[<>:"/\\|?*\x00-\x1f]')

# Online service endpoints
MUSICBRAINZ_API = "https://musicbrainz.org/ws/2"
DISCID_LOOKUP_API = "https://musicbrainz.org/ws/2/discid"


class OnlineDiscIdentifier:
    """Handles online disc identification using various databases."""
    
    def __init__(self, config: Optional[Dict] = None):
        self.config = config or {}
        
        # Read TMDB API key from environment variable first, fall back to config
        self.tmdb_api_key = os.environ.get('TMDB_API_KEY', '')
        
        # If not in environment, try config file (for backward compatibility)
        if not self.tmdb_api_key:
            self.tmdb_api_key = self.config.get('metadata', {}).get('tmdb_api_key', '')
            if self.tmdb_api_key:
                logger.warning("Reading TMDB API key from config file. "
                             "Consider using TMDB_API_KEY environment variable instead for better security.")
        
        # Check if metadata lookup is enabled
        lookup_enabled = self.config.get('metadata', {}).get('lookup_enabled', True)
        
        if self.tmdb_api_key and TMDB_AVAILABLE:
            tmdb.API_KEY = self.tmdb_api_key
            logger.info("TMDB API configured for metadata lookup")
        elif not self.tmdb_api_key and lookup_enabled:
            logger.info("TMDB API key not set. Set TMDB_API_KEY environment variable or add to config file.")
    
    def calculate_disc_id(self, disc_path: str) -> Optional[str]:
        """
        Calculate disc ID from the physical disc.
        This can be used for MusicBrainz lookups.
        
        Args:
            disc_path: Path to disc device
            
        Returns:
            Disc ID string or None if calculation fails
        """
        if not DISCID_AVAILABLE:
            logger.debug("discid library not available, skipping disc ID calculation. "
                        "Install: pip install discid (requires libdiscid)")
            return None
        
        try:
            import discid
            disc = discid.read(disc_path)
            logger.info(f"Calculated disc ID: {disc.id}")
            logger.info(f"  First track: {disc.first_track_num}, Last track: {disc.last_track_num}")
            return disc.id
        except Exception as e:
            logger.debug(f"Could not calculate disc ID: {e}")
            return None
    
    def lookup_disc_musicbrainz(self, disc_id: str) -> Optional[Dict]:
        """
        Look up disc information from MusicBrainz.
        Primarily useful for audio CDs but can provide disc metadata.
        
        Args:
            disc_id: Disc ID string
            
        Returns:
            Disc metadata dictionary or None
        """
        if not REQUESTS_AVAILABLE:
            return None
        
        try:
            headers = {
                'User-Agent': 'MediaEncoding/1.0 (https://github.com/mapitman/media-encoding)'
            }
            
            url = f"{MUSICBRAINZ_API}/discid/{disc_id}"
            params = {'fmt': 'json', 'inc': 'recordings+artist-credits'}
            
            response = requests.get(url, headers=headers, params=params, timeout=10)
            
            if response.status_code == 200:
                data = response.json()
                logger.info(f"Found disc in MusicBrainz: {data.get('title', 'Unknown')}")
                return data
            else:
                logger.debug(f"Disc not found in MusicBrainz: {response.status_code}")
                return None
                
        except Exception as e:
            logger.debug(f"MusicBrainz lookup failed: {e}")
            return None
    
    def search_tmdb_movie(self, title: str, year: Optional[int] = None) -> Optional[Dict]:
        """
        Search for movie metadata on TMDB.
        
        Args:
            title: Movie title to search for
            year: Optional release year for better matching
            
        Returns:
            Movie metadata dictionary or None
        """
        if not TMDB_AVAILABLE or not self.tmdb_api_key:
            logger.debug("TMDB search not available (missing library or API key)")
            return None
        
        try:
            search = tmdb.Search()
            
            # Search for the movie
            if year:
                response = search.movie(query=title, year=year)
            else:
                response = search.movie(query=title)
            
            if search.results:
                # Get the first (best) match
                result = search.results[0]
                
                # Get detailed information
                movie = tmdb.Movies(result['id'])
                info = movie.info()
                
                # Extract year from release_date safely
                year = None
                release_date = info.get('release_date', '')
                if release_date and len(release_date) >= 4:
                    try:
                        year = int(release_date[:4])
                    except ValueError:
                        pass
                
                logger.info(f"Found movie on TMDB: {info.get('title')} ({year or 'Unknown'})")
                
                return {
                    'title': info.get('title'),
                    'original_title': info.get('original_title'),
                    'year': year,
                    'overview': info.get('overview'),
                    'genres': [g['name'] for g in info.get('genres', [])],
                    'runtime': info.get('runtime'),
                    'imdb_id': info.get('imdb_id'),
                    'tmdb_id': info.get('id'),
                    'poster_path': info.get('poster_path'),
                    'backdrop_path': info.get('backdrop_path'),
                    'vote_average': info.get('vote_average'),
                    'type': 'movie'
                }
            else:
                logger.info(f"No TMDB results found for: {title}")
                return None
                
        except Exception as e:
            logger.warning(f"TMDB movie search failed: {e}")
            return None
    
    def search_tmdb_tv(self, title: str, year: Optional[int] = None) -> Optional[Dict]:
        """
        Search for TV series metadata on TMDB.
        
        Args:
            title: TV series title to search for
            year: Optional first air year for better matching
            
        Returns:
            TV series metadata dictionary or None
        """
        if not TMDB_AVAILABLE or not self.tmdb_api_key:
            logger.debug("TMDB search not available (missing library or API key)")
            return None
        
        try:
            search = tmdb.Search()
            
            # Search for the TV series
            if year:
                response = search.tv(query=title, first_air_date_year=year)
            else:
                response = search.tv(query=title)
            
            if search.results:
                # Get the first (best) match
                result = search.results[0]
                
                # Get detailed information
                tv = tmdb.TV(result['id'])
                info = tv.info()
                
                # Extract year from first_air_date safely
                year = None
                first_air_date = info.get('first_air_date', '')
                if first_air_date and len(first_air_date) >= 4:
                    try:
                        year = int(first_air_date[:4])
                    except ValueError:
                        pass
                
                logger.info(f"Found TV series on TMDB: {info.get('name')} ({year or 'Unknown'})")
                
                return {
                    'title': info.get('name'),
                    'original_title': info.get('original_name'),
                    'year': year,
                    'overview': info.get('overview'),
                    'genres': [g['name'] for g in info.get('genres', [])],
                    'number_of_seasons': info.get('number_of_seasons'),
                    'number_of_episodes': info.get('number_of_episodes'),
                    'tmdb_id': info.get('id'),
                    'poster_path': info.get('poster_path'),
                    'backdrop_path': info.get('backdrop_path'),
                    'vote_average': info.get('vote_average'),
                    'episode_run_time': info.get('episode_run_time'),
                    'type': 'tv'
                }
            else:
                logger.info(f"No TMDB results found for TV series: {title}")
                return None
                
        except Exception as e:
            logger.warning(f"TMDB TV search failed: {e}")
            return None


class DiscRipper:
    """Handles disc ripping and encoding operations."""
    
    def __init__(self, output_dir: str, temp_dir: Optional[str] = None, config: Optional[Dict] = None):
        self.output_dir = Path(output_dir)
        # Default temp dir within output if not provided
        self.temp_dir = Path(temp_dir) if temp_dir else self.output_dir / ".makemkv"
        self.output_dir.mkdir(parents=True, exist_ok=True)
        self.temp_dir.mkdir(parents=True, exist_ok=True)
        self.config = config or {}
        
        # Initialize online disc identifier
        self.online_identifier = OnlineDiscIdentifier(self.config)

    @staticmethod
    def human_bytes(n: int) -> str:
        """Return human-readable bytes (e.g., 12.3 GB)."""
        try:
            units = ['B', 'KB', 'MB', 'GB', 'TB']
            size = float(n)
            idx = 0
            while size >= 1024 and idx < len(units) - 1:
                size /= 1024.0
                idx += 1
            return f"{size:.1f} {units[idx]}"
        except Exception:
            return str(n)

    def get_free_space_bytes(self, path: Path) -> int:
        """Get free space available on the filesystem containing path."""
        try:
            usage = shutil.disk_usage(path)
            return int(usage.free)
        except Exception:
            return 0

    def estimate_required_space_bytes(self, disc_info: Dict, title_ids: List[int]) -> int:
        """Estimate total bytes required to rip selected titles (raw MKV sizes)."""
        titles = disc_info.get('titles', [])
        id_set = set(str(t) for t in title_ids)
        total = 0
        for t in titles:
            if str(t.get('id')) in id_set:
                total += int(t.get('size', 0))
        # Add 10% overhead for container and temp files
        return int(total * 1.1)
    
    @staticmethod
    def load_config(config_path: str) -> Optional[Dict]:
        """
        Load configuration from YAML file.
        
        Args:
            config_path: Path to YAML configuration file
            
        Returns:
            Configuration dictionary or None if loading fails
        """
        if not YAML_AVAILABLE:
            logger.error("PyYAML not installed. Cannot load configuration file.")
            logger.error("Install with: pip install pyyaml")
            return None
        
        try:
            with open(config_path, 'r') as f:
                config = yaml.safe_load(f)
                logger.info(f"Loaded configuration from {config_path}")
                return config
        except FileNotFoundError:
            logger.error(f"Configuration file not found: {config_path}")
            return None
        except yaml.YAMLError as e:
            logger.error(f"Error parsing YAML configuration: {e}")
            return None
        except Exception as e:
            logger.error(f"Error loading configuration: {e}")
            return None
        
    def check_dependencies(self) -> bool:
        """Check if required tools are installed."""
        tools = ['makemkvcon', 'ffmpeg', 'ffprobe']
        missing = []
        
        for tool in tools:
            if shutil.which(tool) is None:
                missing.append(tool)
                
        if missing:
            logger.error(f"Missing required tools: {', '.join(missing)}")
            logger.error("Please install makemkv and ffmpeg before running this script")
            return False
        return True
    
    def scan_disc(self, disc_path: str = "disc:0") -> Optional[Dict]:
        """
        Scan disc and get information about available titles.
        
        Args:
            disc_path: Path to disc (e.g., "disc:0" for first drive or "/dev/sr0")
            
        Returns:
            Dictionary containing disc information or None if scan fails
        """
        logger.info(f"💿 Scanning disc at {disc_path}...")
        
        try:
            # Run makemkvcon info to get disc information
            result = subprocess.run(
                ['makemkvcon', '-r', 'info', disc_path],
                capture_output=True,
                text=True,
                timeout=300
            )
            
            if result.returncode != 0:
                logger.error(f"Failed to scan disc: {result.stderr}")
                return None
            
            # Parse the output
            disc_info = self._parse_makemkv_info(result.stdout)
            logger.info(f"📊 Found {len(disc_info.get('titles', []))} titles on disc")
            
            return disc_info
            
        except subprocess.TimeoutExpired:
            logger.error("Disc scan timed out")
            return None
        except Exception as e:
            logger.error(f"Error scanning disc: {e}")
            return None
    
    def _parse_makemkv_info(self, output: str) -> Dict:
        """Parse makemkvcon info output."""
        disc_info = {
            'titles': [],
            'disc_name': '',
            'disc_type': ''
        }
        
        current_title = None
        
        for line in output.split('\n'):
            # Title information
            if line.startswith('TINFO:'):
                parts = line.split(',')
                if len(parts) >= 4:
                    title_id = parts[0].split(':')[1]
                    info_type = parts[1]
                    value = ','.join(parts[3:]).strip('"')
                    
                    if current_title is None or current_title['id'] != title_id:
                        current_title = {
                            'id': title_id,
                            'duration': 0,
                            'size': 0,
                            'chapters': 0,
                            'name': '',
                            'video_tracks': [],
                            'audio_tracks': [],
                            'subtitle_tracks': []
                        }
                        disc_info['titles'].append(current_title)
                    
                    # Map info types
                    if info_type == '2':  # Name
                        current_title['name'] = value
                    elif info_type == '9':  # Duration
                        current_title['duration'] = self._parse_duration(value)
                    elif info_type == '10':  # Size in bytes
                        try:
                            current_title['size'] = int(value)
                        except ValueError:
                            pass
                    elif info_type == '8':  # Chapter count
                        try:
                            current_title['chapters'] = int(value)
                        except ValueError:
                            pass
            
            # Stream information
            elif line.startswith('SINFO:'):
                parts = line.split(',')
                if len(parts) >= 5:
                    title_id = parts[0].split(':')[1].split(',')[0]
                    stream_id = parts[1]
                    info_type = parts[2]
                    value = ','.join(parts[4:]).strip('"')
                    
                    # Find the title
                    title = next((t for t in disc_info['titles'] if t['id'] == title_id), None)
                    if title and info_type == '1':  # Stream type
                        if 'Video' in value:
                            title['video_tracks'].append({'id': stream_id, 'type': 'video'})
                        elif 'Audio' in value:
                            title['audio_tracks'].append({'id': stream_id, 'type': 'audio', 'language': ''})
                        elif 'Subtitle' in value:
                            title['subtitle_tracks'].append({'id': stream_id, 'type': 'subtitle', 'language': ''})
            
            # Disc name
            elif line.startswith('CINFO:2'):
                match = re.search(r'"([^"]+)"', line)
                if match:
                    disc_info['disc_name'] = match.group(1)
        
        return disc_info
    
    def _parse_duration(self, duration_str: str) -> int:
        """Parse duration string (HH:MM:SS) to seconds."""
        try:
            parts = duration_str.split(':')
            if len(parts) == 3:
                hours, minutes, seconds = map(int, parts)
                return hours * 3600 + minutes * 60 + seconds
        except (ValueError, AttributeError):
            pass
        return 0
    
    def identify_main_content(self, disc_info: Dict, is_tv_series: bool = False) -> List[int]:
        """
        Identify main movie or TV episodes from disc titles.
        
        Args:
            disc_info: Dictionary containing disc information
            is_tv_series: True if this is a TV series disc
            
        Returns:
            List of title IDs to rip
        """
        titles = disc_info.get('titles', [])
        
        if not titles:
            return []
        
        if is_tv_series:
            # For TV series, find all episodes (typically 20-60 minutes each)
            episodes = []
            for title in titles:
                duration = title.get('duration', 0)
                if (MIN_EPISODE_DURATION_SECONDS <= duration <= MAX_EPISODE_DURATION_SECONDS 
                    and title.get('chapters', 0) >= MIN_EPISODE_CHAPTERS):
                    episodes.append(int(title['id']))
            
            # Sort by title ID to maintain episode order
            episodes.sort()
            logger.info(f"Identified {len(episodes)} TV episodes")
            return episodes
        else:
            # For movies, find the longest title (main feature)
            # Filter titles with some minimum duration first to avoid errors
            valid_titles = [t for t in titles if t.get('duration', 0) > 0]
            
            if not valid_titles:
                logger.warning("No valid titles with duration found")
                return []
            
            longest_title = max(valid_titles, key=lambda t: t.get('duration', 0))
            
            # Main movie should be at least 45 minutes
            if longest_title.get('duration', 0) >= MIN_MOVIE_DURATION_SECONDS:
                logger.info(f"🎬 Identified main movie: Title {longest_title['id']} "
                          f"({longest_title.get('duration', 0) // 60} minutes)")
                return [int(longest_title['id'])]
            else:
                logger.warning("No title found that meets minimum duration for a movie")
                return []
    
    def rip_titles(self, disc_path: str, title_ids: List[int], 
                   output_prefix: str = "title",
                   disc_info: Optional[Dict] = None) -> List[Path]:
        """
        Rip specified titles from disc.
        
        Args:
            disc_path: Path to disc
            title_ids: List of title IDs to rip
            output_prefix: Prefix for output filenames
            
        Returns:
            List of paths to ripped files
        """
        ripped_files = []
        
        for title_id in title_ids:
            # Log current free space before each title rip
            free_temp = self.get_free_space_bytes(self.temp_dir)
            free_out = self.get_free_space_bytes(self.output_dir)
            logger.info(
                f"💾 Free space - temp: {self.human_bytes(free_temp)}, output: {self.human_bytes(free_out)}"
            )
            # If we have disc_info, estimate title size
            est_title_bytes = None
            expected_file = None
            expected_name = None
            if disc_info:
                try:
                    t = next((x for x in disc_info.get('titles', []) if int(x.get('id', -1)) == int(title_id)), None)
                    if t and t.get('size'):
                        est_title_bytes = int(t.get('size'))
                    # Build expected filename based on title name
                    expected_name = t.get('name', '') if t else ''
                    if expected_name:
                        safe_name = FILENAME_SANITIZE_PATTERN.sub('', expected_name).strip()
                        if safe_name:
                            expected_file = self.temp_dir / f"{safe_name}_t{title_id:02d}.mkv"
                except Exception:
                    est_title_bytes = None
            if est_title_bytes:
                logger.info(f"📦 Estimated title {title_id} size: {self.human_bytes(est_title_bytes)}")
            if est_title_bytes and free_temp < int(est_title_bytes * 1.05):
                logger.warning(
                    f"Temp space may be insufficient for title {title_id}: "
                    f"required≈{self.human_bytes(int(est_title_bytes*1.05))}, free={self.human_bytes(free_temp)}"
                )
            progress_bar = None
            console = None
            if RICH_AVAILABLE:
                import os
                def _get_rows_cols():
                    try:
                        fd = os.open('/dev/tty', os.O_RDONLY)
                        try:
                            buf = struct.pack('hhhh', 0, 0, 0, 0)
                            res = fcntl.ioctl(fd, termios.TIOCGWINSZ, buf)
                            rows, cols, _, _ = struct.unpack('hhhh', res)
                            if rows and cols:
                                return int(rows), int(cols)
                        finally:
                            os.close(fd)
                    except Exception:
                        pass
                    try:
                        env_cols = int(os.environ.get('COLUMNS', '0'))
                        env_rows = int(os.environ.get('LINES', '0'))
                        if env_cols > 0 and env_rows > 0:
                            return env_rows, env_cols
                    except Exception:
                        pass
                    try:
                        out = subprocess.run('stty size < /dev/tty', shell=True, capture_output=True, text=True)
                        if out.returncode == 0 and out.stdout.strip():
                            r, c = out.stdout.strip().split()
                            return int(r), int(c)
                    except Exception:
                        pass
                    return 24, 80

                # Create Rich console for all output, write to /dev/tty to bypass tee
                try:
                    rows, cols = _get_rows_cols()
                except Exception:
                    rows, cols = (24, 80)

                try:
                    tty_file = open('/dev/tty', 'w')
                    console = Console(file=tty_file, force_terminal=True, width=cols)
                except Exception:
                    console = Console(force_terminal=True, width=cols)
                    tty_file = None

                progress = Progress(
                    TextColumn("[cyan]{task.description}"),
                    BarColumn(bar_width=None),  # auto size to available width
                    TextColumn("[progress.percentage]{task.percentage:>3.0f}%"),
                    expand=True,
                )
                task_id = progress.add_task(f"Title {title_id}", total=100)
                live = Live(progress, console=console, refresh_per_second=10)
                live.start()
                progress_bar = progress



            try:
                # To avoid interactive overwrite prompts from MakeMKV, remove any existing
                # temp files for this title before starting the rip.
                try:
                    to_delete_patterns = [
                        f"*_t{title_id:02d}.mkv",
                        f"title_t{title_id:02d}.mkv"
                    ]
                    for pattern in to_delete_patterns:
                        for existing in self.temp_dir.glob(pattern):
                            logger.debug(f"Removing existing temp file to avoid prompt: {existing}")
                            try:
                                existing.unlink()
                            except Exception:
                                pass
                except Exception:
                    pass

                # Stream makemkvcon output to provide real-time progress
                proc = subprocess.Popen(
                    [
                        'stdbuf', '-oL', '-eL', 'makemkvcon',
                        '--messages=-stdout', '--progress=-stdout',
                        '--cache=128',
                        '-r', 'mkv',
                        disc_path, str(title_id), str(self.temp_dir)
                    ],
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    stdin=subprocess.DEVNULL,
                    text=True,
                    bufsize=1
                )

                rip_start_ts = time.time()
                last_pct_logged = -1
                saving_phase = False
                save_start_ts = None
                last_mkv_size = 0
                # If MakeMKV didn't provide an explicit size estimate, approximate from duration
                # Assume average muxed bitrate ~ 2.5 MB/s (≈20 Mb/s) across video+audio
                if est_title_bytes is None and disc_info:
                    try:
                        t = next((x for x in disc_info.get('titles', []) if int(x.get('id', -1)) == int(title_id)), None)
                        dur = int(t.get('duration', 0)) if t else 0
                        if dur > 0:
                            est_title_bytes = int(dur * 2.5 * 1024 * 1024)
                            # Use tqdm-aware logging to avoid interfering with the bar line
                            write_log(f"📊 Estimated title {title_id} size (approx.): {self.human_bytes(est_title_bytes)}", 'info')
                    except Exception:
                        pass
                # Helper to print logs using Rich console
                def write_log(msg: str, level: str = 'info'):
                    try:
                        # Use the live console to print above the progress bar
                        live.console.print(msg)
                    except Exception:
                        # Fallback to logger
                        if level == 'info':
                            logger.info(msg)
                        elif level == 'warning':
                            logger.warning(msg)
                        elif level == 'error':
                            logger.error(msg)
                        else:
                            logger.debug(msg)
                while True:
                    # Poll for output with a timeout to allow periodic progress updates
                    ready, _, _ = select.select([proc.stdout], [], [], 0.25)
                    if ready:
                        line = proc.stdout.readline()
                        if not line:
                            if proc.poll() is not None:
                                break
                            # continue to fallback update below
                        else:
                            line = line.strip()

                            # Progress lines from MakeMKV typically start with PRGV:<value>
                            if line.startswith('PRGV:'):
                                try:
                                    raw = int(line.split(':', 1)[1])
                                    # Heuristic scaling: MakeMKV progress often 0..100000
                                    if raw >= 100000:
                                        pct = raw / 1000.0
                                    elif raw > 100:
                                        pct = raw / 100.0
                                    else:
                                        pct = float(raw)

                                    if pct > 100:
                                        pct = 100.0
                                    pct_int = int(pct)
                                    if progress_bar and task_id is not None:
                                        progress.update(task_id, completed=pct_int)
                                    # Log progress for every 1% increment
                                    if pct_int != last_pct_logged and pct_int % 1 == 0:
                                        if COLORAMA_AVAILABLE:
                                            write_log(f"{Fore.CYAN}Ripping title {title_id}: {pct_int}%{Style.RESET_ALL}", 'info')
                                        else:
                                            write_log(f"Ripping title {title_id}: {pct_int}%", 'info')
                                        last_pct_logged = pct_int
                                except Exception:
                                    logger.debug(f"Could not parse progress line: {line}")
                                    pass
                            elif line.startswith('MSG:'):
                                # Parse MakeMKV message and replace %1, %2 placeholders with provided parameters
                                msg_text = self._parse_makemkv_msg(line)
                                write_log(msg_text, 'info')
                                # If MakeMKV reports space errors, add actual free space info
                                low_space_markers = ('ENOSPC', 'No space', 'not enough space')
                                if any(m in msg_text for m in low_space_markers):
                                    free_temp = self.get_free_space_bytes(self.temp_dir)
                                    free_out = self.get_free_space_bytes(self.output_dir)
                                    write_log(
                                        f"Space check -> temp: {self.human_bytes(free_temp)}, output: {self.human_bytes(free_out)}",
                                        'warning'
                                    )
                            elif line.startswith('PRGC:'):
                                # Progress caption, show as info and update bar description
                                caption_match = re.search(r'"([^"]+)"', line)
                                if caption_match:
                                    caption_text = caption_match.group(1)
                                    write_log(f"{caption_text}", 'info')
                                    if progress_bar and task_id is not None:
                                        # Show caption verbatim; saving descriptions are handled separately
                                        progress.update(task_id, description=caption_text)
                                    if 'Saving to MKV file' in caption_text or 'Saving' in caption_text:
                                        saving_phase = True
                                        save_start_ts = time.time()
                            else:
                                # Filter out robot mode protocol lines (PRGT, DRV, TCOUNT, CINFO, SINFO, etc.)
                                robot_prefixes = ('PRGT:', 'DRV:', 'TCOUNT:', 'CINFO:', 'SINFO:', 'TINFO:', 'STRACK:', 'ATRACK:', 'VTRACK:')
                                if not line.startswith(robot_prefixes):
                                    # All other MakeMKV output—route through Rich if available
                                    if live:
                                        live.console.print(line)
                                    else:
                                        logger.debug(line)

                    # Fallback progress update based on growing MKV file size
                    if progress_bar:
                        try:
                            mkv_candidates = []
                            if expected_file and expected_file.exists():
                                mkv_candidates = [expected_file]
                            if not mkv_candidates:
                                mkv_candidates = list(self.temp_dir.glob(f"*_t{title_id:02d}.mkv"))
                            if not mkv_candidates:
                                mkv_candidates = list(self.temp_dir.glob(f"title_t{title_id:02d}.mkv"))
                            # Include common temporary file patterns that MakeMKV might use
                            if not mkv_candidates:
                                tmp_patterns = ["*.mkv.tmp", "*.mkv.part", "*.tmp", "*.partial"]
                                for pat in tmp_patterns:
                                    mkv_candidates.extend(list(self.temp_dir.glob(pat)))
                            # If specific patterns not found, pick newest MKV created since rip or save start
                            if not mkv_candidates:
                                threshold_ts = save_start_ts or rip_start_ts
                                mkv_candidates = [
                                    p for p in self.temp_dir.glob("*.mkv")
                                    if p.stat().st_mtime >= (threshold_ts - 1)
                                ]
                                mkv_candidates.sort(key=lambda p: p.stat().st_mtime, reverse=True)

                            # Update progress bar based on file size during saving phase
                            if mkv_candidates:
                                current_size = mkv_candidates[0].stat().st_size
                                if est_title_bytes and est_title_bytes > 0:
                                    pct_guess = int((current_size / est_title_bytes) * 100)
                                    if pct_guess > 100:
                                        pct_guess = 100
                                    # During saving phase, always update bar based on file size (trust the estimate)
                                    # Otherwise only update if percentage increased (reading phase)
                                    last_completed = getattr(progress, '_last_completed', 0) if progress_bar else 0
                                    if saving_phase or pct_guess > last_completed:
                                        if progress_bar and task_id is not None:
                                            progress.update(task_id, completed=pct_guess)
                                            if hasattr(progress, '_last_completed'):
                                                progress._last_completed = pct_guess
                                            else:
                                                progress._last_completed = pct_guess
                                            # Update bar description to show byte counts during saving
                                            if saving_phase and current_size != last_mkv_size:
                                                current_h = self.human_bytes(current_size)
                                                total_h = self.human_bytes(est_title_bytes)
                                                target_name = mkv_candidates[0].name if mkv_candidates else f"title_{title_id:02d}.mkv"
                                                progress.update(task_id, description=f"Saving Title {title_id} to {target_name} ({current_h}/{total_h})")
                                                last_mkv_size = current_size
                                        if pct_guess != last_pct_logged and pct_guess % 1 == 0:
                                            last_pct_logged = pct_guess
                                else:
                                    # No estimate available; keep bar active at 99% but show size growth in desc
                                    last_completed = getattr(progress, '_last_completed', 0) if progress_bar else 0
                                    if last_completed < 99:
                                        if progress_bar and task_id is not None:
                                            progress.update(task_id, completed=99)
                                            progress._last_completed = 99
                                            if saving_phase and current_size != last_mkv_size:
                                                current_h = self.human_bytes(current_size)
                                                target_name = mkv_candidates[0].name if mkv_candidates else f"title_{title_id:02d}.mkv"
                                                progress.update(task_id, description=f"Saving Title {title_id} to {target_name} ({current_h})")
                                                last_mkv_size = current_size
                            else:
                                # No file detected yet; keep gentle activity
                                last_completed = getattr(progress, '_last_completed', 0) if progress_bar else 0
                                if progress_bar and task_id is not None and last_completed < 99:
                                    progress.update(task_id, completed=min(99, last_completed + 1))
                                    progress._last_completed = min(99, last_completed + 1)
                        except Exception:
                            pass

                return_code = proc.wait()
                if progress_bar and task_id is not None:
                    progress.update(task_id, completed=100)
                    live.stop()
                    if tty_file:
                        tty_file.close()
                    # Restore previous SIGWINCH handler
                    try:
                        if prev_sigwinch is not None:
                            signal.signal(signal.SIGWINCH, prev_sigwinch)
                    except Exception:
                        pass

                if return_code == 0:
                    # Find the created file
                    mkv_files = list(self.temp_dir.glob(f"*_t{title_id:02d}.mkv"))
                    if not mkv_files:
                        mkv_files = list(self.temp_dir.glob(f"title_t{title_id:02d}.mkv"))
                    if not mkv_files:
                        mkv_files = sorted(self.temp_dir.glob("*.mkv"), key=lambda p: p.stat().st_mtime)
                        if mkv_files:
                            mkv_files = [mkv_files[-1]]

                    if mkv_files:
                        ripped_file = mkv_files[0]
                        logger.info(f"✅ Successfully ripped to {ripped_file}")
                        ripped_files.append(ripped_file)
                    else:
                        logger.error(f"Could not find ripped file for title {title_id}")
                else:
                    logger.error(f"Failed to rip title {title_id} (exit code {return_code})")

            except Exception as e:
                logger.error(f"Error ripping title {title_id}: {e}")
        
        return ripped_files

    def _parse_makemkv_msg(self, line: str) -> str:
        """Parse a MakeMKV MSG: line, replacing %1, %2 placeholders.

        Example: MSG:3020,2,0,"Saving title %1 of %2",1,20 -> "Saving title 1 of 20"
        """
        try:
            # Strip leading 'MSG:'
            payload = line[4:] if line.startswith('MSG:') else line
            # Use CSV reader to handle quoted fields and commas
            fields = next(csv.reader([payload], escapechar='\\'))
            if len(fields) < 4:
                return line
            text = fields[3]
            params = fields[4:]
            # Replace %1..%n with params
            for idx, val in enumerate(params, start=1):
                text = text.replace(f"%{idx}", val)
            return text
        except Exception:
            return line
    
    def analyze_file(self, file_path: Path) -> Optional[Dict]:
        """Analyze media file to get track information."""
        logger.info(f"Analyzing {file_path.name}...")
        
        try:
            result = subprocess.run(
                ['ffprobe', '-v', 'quiet', '-print_format', 'json',
                 '-show_streams', '-show_format', str(file_path)],
                capture_output=True,
                text=True,
                timeout=60
            )
            
            if result.returncode == 0:
                return json.loads(result.stdout)
            else:
                logger.error(f"Failed to analyze file: {result.stderr}")
                return None
                
        except Exception as e:
            logger.error(f"Error analyzing file: {e}")
            return None
    
    def encode_file(self, input_file: Path, output_file: Path, 
                   include_english_subtitles: bool = True) -> bool:
        """
        Re-encode file with optimal settings.
        
        Args:
            input_file: Path to input MKV file
            output_file: Path to output file
            include_english_subtitles: Whether to include English subtitles
            
        Returns:
            True if encoding succeeded, False otherwise
        """
        logger.info(f"Encoding {input_file.name} to {output_file.name}...")
        
        # Analyze input file
        file_info = self.analyze_file(input_file)
        if not file_info:
            return False
        
        streams = file_info.get('streams', [])
        
        # Build ffmpeg command
        cmd = ['ffmpeg', '-i', str(input_file)]
        
        # Video: copy highest resolution video stream
        video_streams = [s for s in streams if s.get('codec_type') == 'video']
        if video_streams:
            # Sort by resolution (width * height)
            video_streams.sort(
                key=lambda s: s.get('width', 0) * s.get('height', 0),
                reverse=True
            )
            video_idx = video_streams[0].get('index', 0)
            cmd.extend(['-map', f'0:{video_idx}', '-c:v', 'copy'])
        
        # Audio: include stereo and surround if available
        audio_streams = [s for s in streams if s.get('codec_type') == 'audio']
        audio_map_idx = 0
        
        for audio_stream in audio_streams:
            channels = audio_stream.get('channels', 0)
            language = audio_stream.get('tags', {}).get('language', '')
            
            # Include English audio or if language is unspecified
            if language in ('eng', 'en', '') or not language:
                stream_idx = audio_stream.get('index', 0)
                
                # Include if stereo (2 channels) or surround (6+ channels)
                if channels == 2 or channels >= 6:
                    cmd.extend(['-map', f'0:{stream_idx}'])
                    cmd.extend([f'-c:a:{audio_map_idx}', 'copy'])
                    audio_map_idx += 1
        
        # Subtitles: include English subtitles if requested
        if include_english_subtitles:
            subtitle_streams = [s for s in streams if s.get('codec_type') == 'subtitle']
            subtitle_map_idx = 0
            
            for sub_stream in subtitle_streams:
                language = sub_stream.get('tags', {}).get('language', '')
                
                # Include English subtitles
                if language in ('eng', 'en'):
                    stream_idx = sub_stream.get('index', 0)
                    cmd.extend(['-map', f'0:{stream_idx}'])
                    cmd.extend([f'-c:s:{subtitle_map_idx}', 'copy'])
                    subtitle_map_idx += 1
        
        # Output file
        cmd.extend(['-y', str(output_file)])
        
        logger.info(f"Encoding command: {' '.join(cmd)}")
        
        try:
            result = subprocess.run(
                cmd,
                capture_output=True,
                text=True,
                timeout=7200  # 2 hours timeout
            )
            
            if result.returncode == 0:
                logger.info(f"Successfully encoded to {output_file}")
                return True
            else:
                logger.error(f"Encoding failed: {result.stderr}")
                return False
                
        except subprocess.TimeoutExpired:
            logger.error("Encoding timed out")
            return False
        except Exception as e:
            logger.error(f"Error encoding file: {e}")
            return False
    
    def lookup_metadata(self, title: str, is_tv_series: bool = False,
                       year: Optional[int] = None, disc_path: Optional[str] = None) -> Optional[Dict]:
        """
        Look up metadata for the media online using TMDB and disc identification.
        
        Args:
            title: Title to search for
            is_tv_series: True if searching for TV series
            year: Optional year for better matching
            disc_path: Optional disc path for disc ID calculation
            
        Returns:
            Dictionary with metadata or None
        """
        logger.info(f"Looking up metadata for: {title}")
        
        # Check if metadata lookup is enabled in config
        metadata_config = self.config.get('metadata', {})
        lookup_enabled = metadata_config.get('lookup_enabled', True)
        
        if not lookup_enabled:
            logger.info("Metadata lookup disabled in configuration")
            return {
                'title': title,
                'year': year,
                'type': 'tv' if is_tv_series else 'movie'
            }
        
        # Try disc identification first if disc path is provided
        if disc_path and REQUESTS_AVAILABLE:
            disc_id = self.online_identifier.calculate_disc_id(disc_path)
            if disc_id:
                disc_info = self.online_identifier.lookup_disc_musicbrainz(disc_id)
                if disc_info:
                    # MusicBrainz found something - primarily for audio CDs
                    # but log it for informational purposes
                    logger.info(f"MusicBrainz disc info: {disc_info.get('title', 'Unknown')}")
        
        # Look up content metadata from TMDB
        metadata = None
        
        if is_tv_series:
            # Search for TV series
            metadata = self.online_identifier.search_tmdb_tv(title, year)
        else:
            # Search for movie
            metadata = self.online_identifier.search_tmdb_movie(title, year)
        
        # If online lookup succeeded, return the metadata
        if metadata:
            return metadata
        
        # Fallback to basic metadata if online lookup failed
        logger.info("Using disc title as fallback")
        return {
            'title': title,
            'year': year,
            'type': 'tv' if is_tv_series else 'movie'
        }
    
    def rename_file(self, file_path: Path, metadata: Dict, 
                   episode_num: Optional[int] = None,
                   season_num: int = 1) -> Path:
        """
        Rename file based on metadata.
        
        Args:
            file_path: Current file path
            metadata: Metadata dictionary
            episode_num: Episode number (for TV series)
            season_num: Season number (for TV series)
            
        Returns:
            New file path
        """
        title = metadata.get('title', 'Unknown')
        year = metadata.get('year', '')
        media_type = metadata.get('type', 'movie')
        
        # Clean title for filename (remove invalid characters)
        safe_title = FILENAME_SANITIZE_PATTERN.sub('', title)
        safe_title = safe_title.strip()
        
        if media_type == 'tv' and episode_num is not None:
            # TV series format: Show Name - S01E01.mkv
            filename = f"{safe_title} - S{season_num:02d}E{episode_num:02d}.mkv"
        else:
            # Movie format: Movie Name (Year).mkv
            if year:
                filename = f"{safe_title} ({year}).mkv"
            else:
                filename = f"{safe_title}.mkv"
        
        new_path = self.output_dir / filename
        
        # Move file
        try:
            file_path.rename(new_path)
            logger.info(f"Renamed to: {new_path}")
            return new_path
        except Exception as e:
            logger.error(f"Failed to rename file: {e}")
            return file_path
    
    def extract_title_from_filename(self, file_path: Path) -> Optional[str]:
        """Extract title from MKV filename (e.g., 'Misery_t00.mkv' -> 'Misery')."""
        try:
            name = file_path.stem  # Remove .mkv extension
            # MakeMKV format: TitleName_t##.mkv or similar
            # Extract everything before the underscore and title index
            match = re.match(r'^(.+?)(?:_t\d+)?$', name)
            if match:
                title = match.group(1).strip()
                if title and title not in ('Unknown', ''):
                    return title
        except Exception:
            pass
        return None

    def process_disc(self, disc_path: str = "disc:0", is_tv_series: bool = False,
                    title: Optional[str] = None, year: Optional[int] = None,
                    season_num: int = 1) -> List[Path]:
        """
        Complete workflow: scan, rip, encode, and rename.
        
        Args:
            disc_path: Path to disc
            is_tv_series: True if this is a TV series disc
            title: Optional title for metadata lookup
            year: Optional year for metadata lookup
            season_num: Season number for TV series
            
        Returns:
            List of final output file paths
        """
        # Check dependencies
        if not self.check_dependencies():
            return []
        
        # Scan disc
        disc_info = self.scan_disc(disc_path)
        if not disc_info:
            logger.error("Failed to scan disc")
            return []
        
        # Use disc name if title not provided
        if not title:
            title = disc_info.get('disc_name', 'Unknown')
        
        # Identify titles to rip
        title_ids = self.identify_main_content(disc_info, is_tv_series)
        if not title_ids:
            logger.error("No suitable titles found on disc")
            return []
        
        # Rip titles (provide disc_info for better diagnostics)
        ripped_files = self.rip_titles(disc_path, title_ids, disc_info=disc_info)
        if not ripped_files:
            logger.error("❌ Failed to rip any titles")
            return []
        
        # Extract title from ripped filename if we don't have a good one yet
        # (MakeMKV often has better title info than disc name)
        if title in ('Unknown', disc_info.get('disc_name', 'Unknown')) and ripped_files:
            extracted_title = self.extract_title_from_filename(ripped_files[0])
            if extracted_title:
                logger.info(f"Using title from filename: {extracted_title}")
                title = extracted_title
        
        # Look up metadata (pass disc_info with disc_id for better identification)
        disc_id = disc_info.get('disc_id', '')
        if disc_id:
            logger.info(f"Using disc ID for metadata lookup: {disc_id}")
        metadata = self.lookup_metadata(title, is_tv_series, year, disc_path)
        if not metadata:
            metadata = {'title': title, 'year': year, 
                       'type': 'tv' if is_tv_series else 'movie'}
        
        # Encode and rename files
        final_files = []
        for idx, ripped_file in enumerate(ripped_files):
            # Generate output filename
            if is_tv_series:
                episode_num = idx + 1
                output_name = f"temp_s{season_num:02d}e{episode_num:02d}.mkv"
            else:
                output_name = f"temp_movie.mkv"
            
            output_file = self.temp_dir / output_name
            
            # Encode
            if self.encode_file(ripped_file, output_file):
                # Rename with metadata
                if is_tv_series:
                    final_file = self.rename_file(output_file, metadata, 
                                                  episode_num=idx + 1,
                                                  season_num=season_num)
                else:
                    final_file = self.rename_file(output_file, metadata)
                
                final_files.append(final_file)
                
                # Clean up original ripped file
                try:
                    ripped_file.unlink()
                except Exception as e:
                    logger.warning(f"Failed to delete temp file {ripped_file}: {e}")
            else:
                logger.error(f"Failed to encode {ripped_file}")
        
        logger.info(f"Processing complete. Output files: {len(final_files)}")
        for f in final_files:
            logger.info(f"  - {f}")
        
        return final_files


def main():
    """Main entry point."""
    parser = argparse.ArgumentParser(
        description='Rip and encode DVDs, Blu-Ray, and UltraHD discs using makemkvcon'
    )
    
    parser.add_argument(
        '--config',
        help='Path to YAML configuration file'
    )
    
    parser.add_argument(
        '--disc',
        default='disc:0',
        help='Disc path (default: disc:0 for first drive)'
    )
    
    parser.add_argument(
        '--output',
        required=True,
        help='Output directory for final files'
    )
    
    parser.add_argument(
        '--temp',
        help='Temporary directory for ripping (default: OUTPUT_DIR/.makemkv)'
    )
    
    parser.add_argument(
        '--tv',
        action='store_true',
        help='Treat disc as TV series (find all episodes instead of main movie)'
    )
    
    parser.add_argument(
        '--title',
        help='Title of movie or TV series (for metadata lookup and naming)'
    )
    
    parser.add_argument(
        '--year',
        type=int,
        help='Release year (for metadata lookup and naming)'
    )
    
    parser.add_argument(
        '--season',
        type=int,
        default=1,
        help='Season number for TV series (default: 1)'
    )
    
    parser.add_argument(
        '--debug',
        action='store_true',
        help='Enable debug logging'
    )
    
    args = parser.parse_args()
    
    if args.debug:
        logging.getLogger().setLevel(logging.DEBUG)
    
    # Load configuration if specified
    config = None
    if args.config:
        config = DiscRipper.load_config(args.config)
        if config is None:
            logger.error("Failed to load configuration file")
            sys.exit(1)
        
        # Apply config defaults if command-line args not specified
        if args.disc == 'disc:0' and config.get('disc', {}).get('default_path'):
            args.disc = config['disc']['default_path']
        
        if args.temp == '/tmp/makemkv' and config.get('disc', {}).get('default_temp_dir'):
            args.temp = config['disc']['default_temp_dir']
    
    # Create ripper instance
    ripper = DiscRipper(args.output, args.temp, config)
    
    # Process disc
    output_files = ripper.process_disc(
        disc_path=args.disc,
        is_tv_series=args.tv,
        title=args.title,
        year=args.year,
        season_num=args.season
    )
    
    if output_files:
        logger.info("Success! Files created:")
        for f in output_files:
            logger.info(f"  {f}")
        sys.exit(0)
    else:
        logger.error("Failed to process disc")
        sys.exit(1)


if __name__ == '__main__':
    try:
        main()
    except KeyboardInterrupt:
        try:
            logger.info("🛑 Rip cancelled by user (Ctrl+C). Exiting cleanly.")
        except Exception:
            print("Rip cancelled by user (Ctrl+C). Exiting cleanly.")
        sys.exit(130)
