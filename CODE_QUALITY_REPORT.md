# Code Quality Analysis Report

**Generated:** January 10, 2026  
**Purpose:** Pre-merge code quality assessment for main branch

## Summary

This report identifies areas where code can be improved through better separation of concerns, reduced duplication, and improved readability. The codebase is generally well-structured with good use of dependency injection and interfaces, but there are several opportunities for refactoring.

---

## Issues & Recommendations

### 1. ~~**DiscRipper Class Violates Single Responsibility Principle**~~ ✅ **COMPLETED**

**Location:** [src/MediaEncoding/DiscRipper.cs](src/MediaEncoding/DiscRipper.cs)

**Issue:** The `DiscRipper` class has grown to 288 lines and handles multiple unrelated responsibilities:
- Disc processing orchestration
- File ripping progress tracking
- MakeMKV output parsing
- File encoding coordination
- File naming and sanitization

**Impact:** High - Makes the class difficult to test, maintain, and understand  
**Related GitHub issue:** [#3](https://github.com/mapitman/media-encoding/issues/3)

**Recommendation:** Extract the following into separate classes:
- `MakeMkvProgressParser` - Handles parsing of MakeMKV robot protocol output (PRGV, PRGC messages)
- `FileNamingService` - Handles file naming conventions and sanitization
- `RipProgressTracker` - Manages progress reporting during rip operations

**Resolution:** ✅ Completed in branch `refactor/discripper-srp`
- Created `MakeMkvProtocol` utility class for quote extraction
- Created `FileNaming` utility class with `SanitizeFileName` and `RenameFile` methods
- Created `MakeMkvOutputHandler` class to encapsulate PRGV/PRGC parsing and progress updates
- Created `IMakeMkvService` interface and `MakeMkvService` implementation to encapsulate makemkvcon invocation
- Decomposed `ProcessDiscAsync()` into focused methods: `PrepareDirectories()`, `ScanDiscAndLookupMetadata()`, `IdentifyTitlesToRip()`, `RipTitlesAsync()`, `EncodeAndRenameAsync()`
- All dependencies now injected via constructor (IDiscScanner, IEncoderService, IMetadataService, IMakeMkvService, IProgressNotifier)
- Verified working with successful test rip

---

### 2. ~~**Duplicate String Extraction Logic**~~ ✅ **COMPLETED**

**Locations:**
- [src/MediaEncoding/DiscRipper.cs](src/MediaEncoding/DiscRipper.cs#L286)
- [src/MediaEncoding/DiscScanner.cs](src/MediaEncoding/DiscScanner.cs#L215)

**Issue:** The `ExtractQuoted` helper method is duplicated in both `DiscRipper` and `DiscScanner` classes with identical implementations.

```csharp
// In DiscRipper.cs
private static string? ExtractQuoted(string line)
{
    var idx = line.IndexOf('"');
    if (idx < 0) return null;
    var idx2 = line.IndexOf('"', idx + 1);
    if (idx2 < 0) return null;
    return line.Substring(idx + 1, idx2 - idx - 1);
}

// In DiscScanner.cs - slightly different implementation
private static string? ExtractQuoted(string line)
{
    var m = Regex.Match(line, "\"([^\"]+)\"");
    return m.Success ? m.Groups[1].Value : null;
}
```

**Impact:** Medium - Code duplication and inconsistent implementations  
**Related GitHub issue:** [#4](https://github.com/mapitman/media-encoding/issues/4)

**Recommendation:** Create a shared `MakeMkvProtocolParser` utility class with common parsing methods.

**Resolution:** ✅ Completed in branch `refactor/discripper-srp`
- Created `MakeMkvProtocol` utility class with shared `ExtractQuoted()` method using regex
- Updated both `DiscRipper` and `DiscScanner` to use `MakeMkvProtocol.ExtractQuoted()`
- Removed duplicate private methods from both classes

---

### ~~3. **DiscScanner Has Mixed Responsibilities**~~ ✅ **COMPLETED**

**Location:** [src/MediaEncoding/DiscScanner.cs](src/MediaEncoding/DiscScanner.cs)

**Issue:** The `ScanDiscAsync` method (249 lines) does three distinct things:
- Parses MakeMKV protocol output
- Displays progress to console (UI concern)
- Builds disc information models (data concern)

The method contains complex inline callbacks with 150+ lines of parsing logic.

**Impact:** High - Very difficult to test, violates separation of concerns

**Recommendation:** Extract components:
- `MakeMkvProtocolParser` - Pure parsing logic without UI
- `ScanProgressPresenter` - Console output formatting
- Keep `DiscScanner` focused on orchestrating the scan operation

**Resolution:** ✅ Completed in branch `refactor/discscanner-srp`
- Created `ScanOutputHandler.cs` to separate parsing from UI concerns
- Injected `IProgressNotifier` into `DiscScanner` for console output abstraction
- Reduced `ScanDiscAsync` from 249 lines to ~20 lines focused on orchestration
- Extracted `BuildDiscInfo()` method for data model creation
- `ScanOutputHandler` handles all protocol parsing (CINFO, TINFO, MSG, DRV)
- All console output now uses `IProgressNotifier` interface instead of direct `AnsiConsole` calls

---

### ~~4. **Console Output Scattered Throughout Business Logic**~~ ✅ **PARTIALLY COMPLETED**

**Locations:**
- ~~[src/MediaEncoding/DiscRipper.cs](src/MediaEncoding/DiscRipper.cs)~~ ✅ - Uses `IProgressNotifier`
- ~~[src/MediaEncoding/DiscScanner.cs](src/MediaEncoding/DiscScanner.cs)~~ ✅ - Uses `IProgressNotifier`
- [src/MediaEncoding/EncoderService.cs](src/MediaEncoding/EncoderService.cs) - 5+ `AnsiConsole.MarkupLine` calls
- [src/MediaEncoding/MetadataService.cs](src/MediaEncoding/MetadataService.cs) - 3+ `AnsiConsole.MarkupLine` calls

**Issue:** UI concerns (console output) are tightly coupled with business logic, making classes hard to test and reuse.

**Impact:** High - Reduces testability and violates separation of concerns  
**Related GitHub issue:** [#5](https://github.com/mapitman/media-encoding/issues/5)

**Recommendation:** Implement a logging/notification abstraction:
- Create `IProgressNotifier` interface
- Implement `ConsoleProgressNotifier` for current behavior
- Inject the notifier into services instead of directly using `AnsiConsole`
- This allows for testing, alternative UIs, and silent modes

**Resolution:** ✅ Partially completed in branches `refactor/discripper-srp` and `refactor/discscanner-srp`
- Created `IProgressNotifier` interface with methods: Info, Success, Warning, Error, Muted, Accent, Highlight, Plain
- Implemented `ConsoleProgressNotifier` with Spectre.Console integration
- Migrated `DiscRipper` and `DiscScanner` to use `IProgressNotifier`
- Registered in DI container
- **Remaining**: EncoderService and MetadataService still use direct AnsiConsole calls

---

### 5. ~~**Long Methods Need Decomposition**~~ ✅ **PARTIALLY COMPLETED**

**Locations:**
- [src/MediaEncoding/DiscRipper.cs](src/MediaEncoding/DiscRipper.cs#L26-L241) - `ProcessDiscAsync` method (215 lines)
- [src/MediaEncoding/DiscScanner.cs](src/MediaEncoding/DiscScanner.cs#L14-L171) - `ScanDiscAsync` method (157 lines)
- [src/MediaEncoding/EncoderService.cs](src/MediaEncoding/EncoderService.cs#L47-L205) - `EncodeAsync` method (158 lines)

**Issue:** These methods are too long and handle multiple concerns within a single method body.  
**Related GitHub issue:** [#6](https://github.com/mapitman/media-encoding/issues/6)

**Impact:** Medium - Reduces readability and makes testing difficult

**Recommendation:**
- **DiscRipper.ProcessDiscAsync**: Extract methods:
  - `PrepareDirectories()`
  - `ScanAndValidateDisc()`
  - `IdentifyTitlesToRip()`
  - `RipTitle()` - the ripping loop body
  - `EncodeAndRenameRippedFiles()`
  
- ~~**DiscScanner.ScanDiscAsync**~~: ✅ Extract methods:
  - ~~`ParseProtocolLine()`~~ - Extracted to `ScanOutputHandler`
  - ~~`HandleProgressMessage()`~~ - Extracted to `ScanOutputHandler`
  - ~~`BuildDiscInfo()`~~ - Extracted as separate method
  
- **EncoderService.EncodeAsync**: Extract methods:
  - `BuildFfmpegArguments()`
  - `SelectStreams()`
  - `HandleEncodingProgress()`

**Resolution:** ✅ Partially completed in branch `refactor/discripper-srp`
- Decomposed `DiscRipper.ProcessDiscAsync()` into: `PrepareDirectories()`, `ScanDiscAndLookupMetadata()`, `IdentifyTitlesToRip()`, `RipTitlesAsync()`, `EncodeAndRenameAsync()`
- Decomposed `DiscScanner.ScanDiscAsync()` by extracting parsing/UI to `ScanOutputHandler` and data building to `BuildDiscInfo()`
- Main methods now read as clear orchestration workflows
- **Remaining:** `EncoderService.EncodeAsync()` still needs decomposition

---

### 6. ~~**Duplicate File Sanitization Logic**~~ ✅ **COMPLETED**

**Locations:**
- [src/MediaEncoding/DiscRipper.cs](src/MediaEncoding/DiscRipper.cs#L275-L280) - `SanitizeFileName` method
- Used in [src/MediaEncoding/DiscRipper.cs](src/MediaEncoding/DiscRipper.cs#L228) and [src/MediaEncoding/DiscRipper.cs](src/MediaEncoding/DiscRipper.cs#L263)

**Issue:** File naming logic is embedded in `DiscRipper` but this is a general utility that could be reused.

**Impact:** Low - Limited reusability

**Recommendation:** Move to a shared `FileNamingService` or utility class.

**Resolution:** ✅ Completed in branch `refactor/discripper-srp`
- Created `FileNaming` static utility class with `SanitizeFileName()` and `RenameFile()` methods
- Updated `DiscRipper` to use `FileNaming.SanitizeFileName()` and `FileNaming.RenameFile()`
- Removed duplicate methods from `DiscRipper`

---

### 7. **Missing Utility Helpers for Duplicate Parsing Logic**

**Locations:**
- [src/MediaEncoding/DiscScanner.cs](src/MediaEncoding/DiscScanner.cs#L220-L227) - `ParseDurationToSeconds`
- [src/MediaEncoding/DiscScanner.cs](src/MediaEncoding/DiscScanner.cs#L229-L239) - `TryParseBytes`
- [src/MediaEncoding/DiscScanner.cs](src/MediaEncoding/DiscScanner.cs#L241-L247) - `NormalizeDiscType`

**Issue:** Multiple utility methods for parsing specific formats are defined as private static methods in `DiscScanner`.

**Impact:** Low - Limited reusability

**Recommendation:** Consider extracting to a `DiscFormatParser` utility class if these might be needed elsewhere.

---

### 8. **Hardcoded Progress Visualization Logic**

**Location:** [src/MediaEncoding/DiscRipper.cs](src/MediaEncoding/DiscRipper.cs#L89-L178)

**Issue:** The progress bar setup and management is deeply embedded in the ripping loop with hardcoded column configurations, making it difficult to customize or test.

**Impact:** Medium - Reduces flexibility and testability

**Recommendation:** Extract progress tracking:
- Create `ProgressBarConfiguration` class
- Extract progress update logic into dedicated methods
- Consider a `IProgressReporter` abstraction

---

### 9. **Complex Nested Callbacks in DiscRipper**

**Location:** [src/MediaEncoding/DiscRipper.cs](src/MediaEncoding/DiscRipper.cs#L132-L164)

**Issue:** The `HandleMakemkvLine` callback within `ProcessDiscAsync` is deeply nested and contains complex logic for parsing and progress updates.

**Impact:** Medium - Hard to understand control flow

**Recommendation:** Extract to a separate method or class (`MakeMkvOutputHandler`) with clear responsibilities.

---

### 10. **Stream Selection Logic Needs Extraction**

**Location:** [src/MediaEncoding/EncoderService.cs](src/MediaEncoding/EncoderService.cs#L207-L223)

**Issue:** `ChooseBestVideo` is the only stream selection method, but the audio/subtitle selection logic is inline in `EncodeAsync`.

**Impact:** Low - Inconsistent design, could be more testable

**Recommendation:** Create a `StreamSelector` class with methods:
- `SelectBestVideo()`
- `SelectAudioStreams()`
- `SelectSubtitleStreams()`

---

### 11. **EncoderService Builds Complex FFmpeg Commands Inline**

**Location:** [src/MediaEncoding/EncoderService.cs](src/MediaEncoding/EncoderService.cs#L52-L106)

**Issue:** The FFmpeg argument building logic spans 50+ lines and mixes stream selection with command construction.

**Impact:** Medium - Hard to test and modify encoding settings

**Recommendation:** Extract to `FfmpegCommandBuilder` class with fluent interface:
```csharp
var command = new FfmpegCommandBuilder()
    .WithInput(inputFile)
    .WithVideo(videoStream, preset: "slow", crf: 22)
    .WithAudioStreams(audioStreams)
    .WithSubtitles(subtitleStreams)
    .Build();
```

---

### 12. **RipOptions Class Has Multiple Parsing Concerns**

**Location:** [src/MediaEncoding/RipOptions.cs](src/MediaEncoding/RipOptions.cs)

**Issue:** While not overly complex, the `ParseArgs` method mixes argument parsing with validation and default value assignment.

**Impact:** Low - Generally acceptable but could be cleaner

**Recommendation:** Consider splitting into:
- `RipOptionsParser` - handles command-line parsing
- `RipOptionsValidator` - validates the options
- Keep `RipOptions` as a pure data class

---

### 13. **Missing Null Safety in Metadata Lookups**

**Location:** [src/MediaEncoding/MetadataService.cs](src/MediaEncoding/MetadataService.cs)

**Issue:** The metadata lookup logic has multiple nested try-catch blocks that silently fail, and the fallback metadata may not always be appropriate.

**Impact:** Low - Works but could be more explicit

**Recommendation:** 
- Create explicit result types (`MetadataResult` with status)
- Log why lookups fail rather than silent catches
- Consider retry logic for transient failures

---

### 14. **Inconsistent Error Handling**

**Locations:** Throughout the codebase

**Issue:** Some methods return null on failure, others return empty collections, and some throw exceptions. There's no consistent error handling strategy.

Examples:
- `ScanDiscAsync` returns `null` on failure
- `AnalyzeAsync` returns `null` on failure
- `ProcessDiscAsync` returns empty list on failure
- `ParseArgs` throws exceptions on invalid input

**Impact:** Medium - Makes error handling unpredictable

**Recommendation:** Establish consistent patterns:
- Use Result types for operations that can fail
- Reserve exceptions for truly exceptional cases
- Document failure modes clearly

---

### 15. **Magic Numbers and Hardcoded Values**

**Locations:**
- [src/MediaEncoding/DiscScanner.cs](src/MediaEncoding/DiscScanner.cs#L185-L195) - Duration thresholds (20, 60, 30 minutes)
- [src/MediaEncoding/EncoderService.cs](src/MediaEncoding/EncoderService.cs#L82-L86) - Encoding parameters (CRF 22, preset slow, bitrate 160k)
- [src/MediaEncoding/DiscRipper.cs](src/MediaEncoding/DiscRipper.cs#L66) - Probe size "400M"

**Issue:** Configuration values are hardcoded throughout the codebase.

**Impact:** Low - Reduces flexibility

**Recommendation:** 
- Move to `AppConfig` or constants classes
- Create `EncodingProfile` classes for different quality settings
- Use `TitleFilterSettings` for duration thresholds

---

### 16. **AppConfig Class Is Not Used**

**Location:** [src/MediaEncoding/AppConfig.cs](src/MediaEncoding/AppConfig.cs)

**Issue:** The `AppConfig` class is registered in DI but never actually injected or used anywhere in the codebase.

**Impact:** Low - Dead code

**Recommendation:** Either integrate it into the services or remove it if not needed.

---

### 17. **Missing Input Validation**

**Locations:**
- [src/MediaEncoding/ProcessRunner.cs](src/MediaEncoding/ProcessRunner.cs) - No validation of fileName or arguments
- [src/MediaEncoding/DiscRipper.cs](src/MediaEncoding/DiscRipper.cs#L26) - Limited validation of options

**Issue:** Methods don't validate inputs, which could lead to confusing errors.

**Impact:** Low - Could improve error messages

**Recommendation:** Add guard clauses and parameter validation at service boundaries.

---

## Priority Summary

### High Priority (Should fix before merge)
1. Issue #1 - DiscRipper SRP violation
2. Issue #3 - DiscScanner mixed responsibilities
3. Issue #4 - Console output coupling
4. Issue #5 - Long methods

### Medium Priority (Should address soon)
2. Issue #2 - Duplicate extraction logic
8. Issue #8 - Progress visualization coupling
9. Issue #9 - Complex nested callbacks
11. Issue #11 - FFmpeg command building
14. Issue #14 - Inconsistent error handling

### Low Priority (Nice to have)
6. Issue #6 - File sanitization
7. Issue #7 - Utility helpers
10. Issue #10 - Stream selection
12. Issue #12 - RipOptions parsing
13. Issue #13 - Metadata null safety
15. Issue #15 - Magic numbers
16. Issue #16 - Unused AppConfig
17. Issue #17 - Input validation

---

## Testing Considerations

The current architecture makes unit testing difficult due to:
- Tight coupling to `AnsiConsole` (Spectre.Console)
- Long methods with multiple responsibilities
- Complex nested callbacks
- Inline progress tracking

After refactoring issues #1, #3, #4, and #5, the codebase will be significantly more testable.

---

## Positive Aspects

The codebase demonstrates several good practices:
- ✅ Good use of dependency injection
- ✅ Interface-based design for main services
- ✅ Consistent naming conventions
- ✅ Good separation of data models
- ✅ Async/await used appropriately
- ✅ Modern C# features (nullable reference types, init properties)

---

## Estimated Refactoring Effort

- **High Priority Issues:** 2-3 days
- **Medium Priority Issues:** 1-2 days  
- **Low Priority Issues:** 1 day

**Total:** ~4-6 days for comprehensive refactoring
