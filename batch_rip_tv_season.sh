#!/bin/bash
#
# Example batch processing script for TV series season
# Customize the list below with your TV series discs and run this script
#
# Usage: ./batch_rip_tv_season.sh
#

set -e

# Configuration - Replace with your TV series information
SERIES_TITLE="Your TV Series Name"
SEASON_NUMBER=1
NUM_DISCS=2  # Number of discs in this season
OUTPUT_DIR="$HOME/TV Shows"
TEMP_DIR="$OUTPUT_DIR/.makemkv"

echo "==================================="
echo "Batch TV Season Ripping Script"
echo "==================================="
echo ""
echo "Series: $SERIES_TITLE"
echo "Season: $SEASON_NUMBER"
echo "Number of discs: $NUM_DISCS"
echo "Output directory: $OUTPUT_DIR"
echo ""

# Create output directory if it doesn't exist
mkdir -p "$OUTPUT_DIR"

# Process each disc
for disc_num in $(seq 1 $NUM_DISCS); do
    echo ""
    echo "==================================="
    echo "Disc $disc_num of $NUM_DISCS"
    echo "==================================="
    echo ""
    echo "Please insert disc $disc_num for '$SERIES_TITLE' Season $SEASON_NUMBER"
    echo "Press Enter to continue... (or Ctrl+C to abort)"
    read -r
    
    # Rip the disc
    ./rip_tv.sh \
        --title "$SERIES_TITLE" \
        --season "$SEASON_NUMBER" \
        --output "$OUTPUT_DIR" \
        --temp "$TEMP_DIR"
    
    if [ $? -eq 0 ]; then
        echo ""
        echo "✓ Successfully ripped disc $disc_num"
        echo ""
        
        if [ $disc_num -lt $NUM_DISCS ]; then
            echo "Please eject the disc and press Enter to continue..."
            read -r
        fi
    else
        echo ""
        echo "✗ Failed to rip disc $disc_num"
        echo ""
        echo "Press Enter to continue to next disc or Ctrl+C to abort..."
        read -r
    fi
done

echo ""
echo "==================================="
echo "Batch processing complete!"
echo "==================================="
echo ""
echo "Episodes have been saved to: $OUTPUT_DIR"
