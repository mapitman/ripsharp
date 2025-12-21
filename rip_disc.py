#!/usr/bin/env python3
"""
Media Disc Ripping and Encoding Script
Rips DVDs, Blu-Ray, and UltraHD discs using makemkvcon and re-encodes them with optimal settings.
"""

import argparse
import json
import logging
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import List, Dict, Optional, Tuple

try:
    import yaml
    YAML_AVAILABLE = True
except ImportError:
    YAML_AVAILABLE = False
    logger = logging.getLogger(__name__)
    logger.warning("PyYAML not installed. Configuration file support disabled. "
                  "Install with: pip install pyyaml")

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

# Constants
MIN_EPISODE_DURATION_SECONDS = 1200  # 20 minutes
MAX_EPISODE_DURATION_SECONDS = 3600  # 60 minutes
MIN_MOVIE_DURATION_SECONDS = 2700    # 45 minutes

# Regex pattern for sanitizing filenames
FILENAME_SANITIZE_PATTERN = re.compile(r'[<>:"/\\|?*\x00-\x1f]')


class DiscRipper:
    """Handles disc ripping and encoding operations."""
    
    def __init__(self, output_dir: str, temp_dir: str = "/tmp/makemkv", config: Optional[Dict] = None):
        self.output_dir = Path(output_dir)
        self.temp_dir = Path(temp_dir)
        self.output_dir.mkdir(parents=True, exist_ok=True)
        self.temp_dir.mkdir(parents=True, exist_ok=True)
        self.config = config or {}
    
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
        logger.info(f"Scanning disc at {disc_path}...")
        
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
            logger.info(f"Found {len(disc_info.get('titles', []))} titles on disc")
            
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
                    and title.get('chapters', 0) >= 1):
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
                logger.info(f"Identified main movie: Title {longest_title['id']} "
                          f"({longest_title.get('duration', 0) // 60} minutes)")
                return [int(longest_title['id'])]
            else:
                logger.warning("No title found that meets minimum duration for a movie")
                return []
    
    def rip_titles(self, disc_path: str, title_ids: List[int], 
                   output_prefix: str = "title") -> List[Path]:
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
            logger.info(f"Ripping title {title_id}...")
            
            try:
                # Use makemkvcon to rip the title
                result = subprocess.run(
                    ['makemkvcon', '-r', 'mkv', disc_path, str(title_id), 
                     str(self.temp_dir)],
                    capture_output=True,
                    text=True,
                    timeout=3600  # 1 hour timeout
                )
                
                if result.returncode == 0:
                    # Find the created file
                    mkv_files = list(self.temp_dir.glob(f"*_t{title_id:02d}.mkv"))
                    if not mkv_files:
                        # Try alternative pattern
                        mkv_files = list(self.temp_dir.glob(f"title_t{title_id:02d}.mkv"))
                    if not mkv_files:
                        # List all new mkv files
                        mkv_files = sorted(self.temp_dir.glob("*.mkv"), 
                                         key=lambda p: p.stat().st_mtime)
                        if mkv_files:
                            mkv_files = [mkv_files[-1]]  # Take the most recent
                    
                    if mkv_files:
                        ripped_file = mkv_files[0]
                        logger.info(f"Successfully ripped to {ripped_file}")
                        ripped_files.append(ripped_file)
                    else:
                        logger.error(f"Could not find ripped file for title {title_id}")
                else:
                    logger.error(f"Failed to rip title {title_id}: {result.stderr}")
                    
            except subprocess.TimeoutExpired:
                logger.error(f"Ripping title {title_id} timed out")
            except Exception as e:
                logger.error(f"Error ripping title {title_id}: {e}")
        
        return ripped_files
    
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
                       year: Optional[int] = None) -> Optional[Dict]:
        """
        Look up metadata for the media online.
        This is a placeholder - in production, you'd integrate with TMDB, TVDB, etc.
        
        Args:
            title: Title to search for
            is_tv_series: True if searching for TV series
            year: Optional year for better matching
            
        Returns:
            Dictionary with metadata or None
        """
        logger.info(f"Looking up metadata for: {title}")
        
        # Placeholder implementation
        # In production, integrate with TMDB API or similar
        logger.warning("Metadata lookup not implemented - using disc title")
        
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
        
        # Rip titles
        ripped_files = self.rip_titles(disc_path, title_ids)
        if not ripped_files:
            logger.error("Failed to rip any titles")
            return []
        
        # Look up metadata
        metadata = self.lookup_metadata(title, is_tv_series, year)
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
        default='/tmp/makemkv',
        help='Temporary directory for ripping (default: /tmp/makemkv)'
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
    main()
