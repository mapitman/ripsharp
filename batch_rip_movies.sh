#!/bin/bash
#
# Example batch processing script for multiple discs
# Customize the list below with your discs and run this script
#
# Usage: ./batch_rip_movies.sh
#

set -e

# Define your movies here (format: "Title:Year")
# Replace these examples with your actual movie collection
MOVIES=(
    "Movie Title 1:2024"
    "Movie Title 2:2023"
    "Movie Title 3:2022"
)

# Configuration
OUTPUT_DIR="$HOME/Movies"
TEMP_DIR="$OUTPUT_DIR/.makemkv"

echo "==================================="
echo "Batch Movie Ripping Script"
echo "==================================="
echo ""
echo "This script will rip ${#MOVIES[@]} movies"
echo "Output directory: $OUTPUT_DIR"
echo ""

# Create output directory if it doesn't exist
mkdir -p "$OUTPUT_DIR"

# Process each movie
for i in "${!MOVIES[@]}"; do
    movie="${MOVIES[$i]}"
    title="${movie%:*}"
    year="${movie#*:}"
    
    echo ""
    echo "==================================="
    echo "Movie $((i+1)) of ${#MOVIES[@]}: $title ($year)"
    echo "==================================="
    echo ""
    echo "Please insert the disc for '$title' and press Enter to continue..."
    echo "(or press Ctrl+C to abort)"
    read -r
    
    # Rip the movie
    ./rip_movie.sh \
        --title "$title" \
        --year "$year" \
        --output "$OUTPUT_DIR" \
        --temp "$TEMP_DIR"
    
    if [ $? -eq 0 ]; then
        echo ""
        echo "✓ Successfully ripped: $title ($year)"
        echo ""
        echo "Please eject the disc and press Enter to continue..."
        read -r
    else
        echo ""
        echo "✗ Failed to rip: $title ($year)"
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
echo "Movies have been saved to: $OUTPUT_DIR"
