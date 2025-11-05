# Maximum Horizon Plugin for NINA

A N.I.N.A. (Nighttime Imaging 'N' Astronomy) plugin that allows users to define custom maximum altitude constraints (upper horizon limits) at various azimuth angles to prevent imaging targets that are obstructed by overhead objects like balconies, roofs, or other structures.

## Features

- **Loop until Maximum Horizon Condition**: Blocks sequence execution when targets exceed maximum altitude constraints, allowing your sequence to loop until the target becomes blocked
- **Real-time Visibility Display**: Condition shows current altitude, maximum altitude, and active profile name in real-time
- **Global Profile Selection**: Set a default horizon profile in plugin options that all conditions use automatically
- **Condition-Level Override**: Optionally select a specific profile for each condition instance
- **Multiple Horizon Profiles**: Create and manage multiple named horizon profiles (e.g., "Home Horizon", "Remote Site", "Backyard Setup")
- **Shareable Profiles**: Horizon profiles are stored in a shared location accessible from all NINA profiles
- **Global Margin Buffer**: Set a safety margin buffer (0-10°) that applies to all maximum altitude checks
- **Multiple Input Methods**:
  - **Manual Entry**: Enter altitude constraints for each azimuth degree (0-359) using a DataGrid
  - **CSV Import**: Import horizon data from CSV files with flexible column naming
  - **Image Import**: Extract horizon profile from images (white = visible sky, black = obstructions)
- **Profile Management**: Create, duplicate, delete, and rename profiles with an intuitive UI
- **Real-time Updates**: Condition automatically updates when global profile selection changes
- **Target Scheduler Integration**: Service interface for integration with Target Scheduler and other planning plugins

## Installation

1. Download the latest release from the [Releases](https://github.com/astro-roro/Maximum-Horizon/releases) page
2. Extract the plugin DLL to `%localappdata%\NINA\Plugins\3.0.0\Maximum Horizon\`
3. Restart NINA
4. The plugin should appear in NINA's plugin manager under Options → Plugins

## Usage

### Creating Horizon Profiles

1. Open **NINA Options → Plugins → Maximum Horizon**
2. Click **"New Profile"** and enter a profile name (e.g., "Home Horizon")
3. Choose an input method:
   - **Manual Entry Tab**: Edit altitude values in the DataGrid for each azimuth degree (0-359)
   - **CSV Import Tab**: Click **"Browse..."** and select a CSV file with format: `Azimuth,MaxAltitude`
   - **Image Import Tab**: Click **"Browse..."** and select an image (360 pixels wide recommended, 1 pixel per degree)
4. Adjust the **Global Margin Buffer** slider (0-10°) to add a safety margin to all altitude checks
5. Click **"Save Profile"** to persist your changes
6. Select your profile from the **"Selected Profile"** dropdown to set it as the default

### Using the Condition

1. Add the **"Loop until Maximum Horizon"** condition to your sequence (found in the **Maximum Horizon** category)
2. **Profile Selection**:
   - Leave the condition's profile dropdown empty to use the global profile selected in Options
   - Or select a specific profile for this condition instance (overrides global setting)
3. Optionally set a **Margin Buffer** (degrees) for this specific condition (in addition to the global margin)
4. The condition will:
   - Display current target altitude vs maximum altitude in real-time
   - Show the active profile name
   - Block sequence execution (break loop) when targets exceed the maximum altitude
   - Allow sequence to continue (continue loop) when targets are visible

### Condition Display

The condition shows the following information in real-time:
- **Visibility Status**: ✓ Visible (green) or ✗ Blocked (red)
- **Current Altitude**: Current target altitude in degrees
- **Max Altitude**: Maximum allowed altitude at current azimuth (with margin applied)
- **Profile Name**: Active horizon profile being used

### CSV Format

CSV files should have the following format:
```csv
Azimuth,MaxAltitude
0,85.5
1,85.2
2,84.8
...
359,85.0
```

**Supported column names**:
- **Azimuth column**: `Azimuth`, `Angle`, `Degrees`, `Az`
- **Altitude column**: `Altitude`, `Height`, `MaxAltitude`, `Max Altitude`, `Max`

The plugin automatically detects the column order and names.

### Image Format

Images should represent a horizon profile where:
- **White pixels** = visible sky (no obstruction, high altitude)
- **Black pixels** = obstructions (low altitude)

**Recommended dimensions**:
- **Width**: 360 pixels (1 pixel per degree azimuth, 0-359)
- **Height**: Any height (represents 0-90 degrees altitude, with top = 90°, bottom = 0°)

The plugin includes a threshold slider to fine-tune the white/black detection.

## Target Scheduler Integration

The plugin exports `IMaximumHorizonService` via MEF for integration with other plugins like Target Scheduler. The service provides methods for checking target visibility at specific times and coordinates.

### Example Usage

```csharp
// Get the service instance
var horizonService = MaximumHorizonServiceAccessor.GetShared();

// Check if a target is visible at a specific time
bool isVisible = await horizonService.IsTargetVisibleAtTimeAsync(
    raHours: 12.5,           // Right Ascension in hours
    decDegrees: 45.0,        // Declination in degrees
    latitude: 40.0,          // Observer latitude
    longitude: -75.0,        // Observer longitude
    utcTime: DateTime.UtcNow,
    profileName: "Home Horizon"  // Optional: use specific profile, or null for default
);

// Get maximum altitude for a specific azimuth
double maxAlt = await horizonService.GetMaximumAltitudeAsync(
    azimuth: 180,            // Azimuth in degrees (0-359)
    profileName: "Home Horizon"
);
```

### Service Methods

- `GetAvailableProfilesAsync()` - Get all available profile names
- `GetProfileAsync(string profileName)` - Get a specific profile
- `IsTargetVisibleAtTimeAsync(...)` - Check visibility from RA/Dec coordinates
- `GetMaximumAltitudeAsync(int azimuth, string profileName)` - Get max altitude for azimuth
- `SelectedProfileName` - Get/set the globally selected profile
- `GlobalMarginBuffer` - Get/set the global margin buffer

## Technical Details

- **Storage Location**: `%localappdata%\NINA\Plugins\MaximumHorizon\Profiles\`
- **Profile Format**: JSON files (one per profile, named `{ProfileName}.json`)
- **Interpolation**: Linear interpolation between defined points for smooth horizon profiles
- **Altitude Range**: 0-90 degrees
- **Azimuth Range**: 0-359 degrees
- **Service Pattern**: Singleton service instance shared across all plugin components
- **Event System**: `ProfilesChanged` and `SettingsChanged` events for reactive updates

## Requirements

- **NINA**: 3.0 or later
- **.NET**: 8.0 or later
- **Windows**: 10 or later

## Building from Source

1. Clone the repository:
   ```bash
   git clone https://github.com/yourusername/NINA.Plugin.MaximumHorizon.git
   cd NINA.Plugin.MaximumHorizon
   ```

2. Open `NINA.Plugin.MaximumHorizon.sln` in Visual Studio 2022 or later

3. Restore NuGet packages:
   ```bash
   dotnet restore
   ```

4. Build the solution:
   ```bash
   dotnet build --configuration Release
   ```

5. Copy the output DLL from `bin\Release\net8.0\` to `%localappdata%\NINA\Plugins\3.0.0\Maximum Horizon\`

## Version

Current version: **1.0.1.2**

## License

MIT License - see [LICENSE](LICENSE) file for details

## Support

For issues, feature requests, or questions:
- **GitHub Issues**: [Open an issue](https://github.com/yourusername/NINA.Plugin.MaximumHorizon/issues)
- **NINA Forums**: [NINA Community Forums](https://nighttime-imaging.eu/)

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## Author

**Rohan Hinton**

## Acknowledgments

Built for the N.I.N.A. (Nighttime Imaging 'N' Astronomy) community.
