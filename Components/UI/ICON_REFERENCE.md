# Icon Component Reference

## Overview
The `Icon.razor` component provides consistent, scalable SVG icons throughout the application, replacing Unicode emojis for reliable cross-platform display.

## Usage

### Basic Usage
```razor
<Icon Name="check" />
```

### With Custom Size
```razor
<Icon Name="search" Size="24" />
```

### With Custom Color
```razor
<Icon Name="alert" Color="#ef4444" />
```

### Combined Properties
```razor
<Icon Name="users" Size="28" Color="#3b82f6" Class="mr-2" />
```

## Available Icons

### Core Actions
- **check** ✅ - Confirmation, success
- **x** ❌ - Close, cancel, error
- **plus** ➕ - Add, create new
- **edit** ✏️ - Edit, modify
- **trash** 🗑️ - Delete, remove
- **save** 💾 - Save changes
- **refresh** 🔄 - Reload, sync

### Navigation & Info
- **search** 🔍 - Search, find
- **info** ℹ️ - Information
- **alert** ⚠️ - Warning
- **eye** 👁️ - View, visibility
- **link** 🔗 - Hyperlink, connection

### Security
- **lock** 🔐 - Locked, secure
- **key** 🔑 - Authentication, access
- **shield** 🛡️ - Protection, security
- **ban** 🚫 - Prohibited, blocked

### Data & Files
- **clipboard** 📋 - List, records
- **folder** 📁 - Directory, files
- **file** 🗂️ - Document
- **database** 🗄️ - Database, storage
- **package** 📦 - Module, deployment
- **cloud** ☁️ - Cloud storage

### System
- **settings** ⚙️ - Configuration
- **tool** 🔧 - Tools, diagnostics
- **power** 🔌 - Connection, power

### People & Time
- **users** 👥 - Users, people
- **calendar** 📅 - Date, schedule
- **timer** ⏱️ - Duration, time
- **clock** ⏳ - Clock, pending
- **award** 🏆 - Achievement, badge

## Icon Mapping (Emoji → Icon Name)

| Old Emoji | New Icon Name | Context |
|-----------|---------------|---------|
| ✅ | check | Success, enabled |
| ❌ | x | Error, cancel, disabled |
| 📋 | clipboard | Lists, records |
| 🔐 | lock | Locked resources |
| 🔑 | key | Keys, primary access |
| 🔍 | search | Search functionality |
| ➕ | plus | Add new items |
| ✏️ | edit | Edit actions |
| 🗑️ | trash | Delete actions |
| 💾 | save | Save operations |
| 🔄 | refresh | Refresh/reload |
| ⏳ | clock | Pending/waiting |
| ⚠️ | alert | Warnings |
| ℹ️ | info | Information |
| 🔌 | power | Connection status |
| 👥 | users | User management |
| 📅 | calendar | Dates |
| ⏱️ | timer | Duration |
| 🏆 | award | Achievements |
| 📦 | package | Packages/modules |
| ☁️ | cloud | Cloud storage |
| 📁 | folder | Folders |
| 🔧 | tool | Tools/diagnostics |
| 🗄️ | database | Database |
| ⚙️ | settings | Settings |
| 🔗 | link | Links |
| 🗂️ | file | Files |
| 🔒 | lock | Locked (alternate) |
| 🛡️ | shield | Security |
| 🚫 | ban | Prohibited |
| 👁️ | eye | View/visibility |

## Color Guidelines

### Status Colors (lgreeny theme)
- Success: `#10b981` (green)
- Error: `#ef4444` (red)
- Warning: `#f59e0b` (orange)
- Info: `#3b82f6` (blue)
- Purple: `#8b5cf6` (purple)
- Teal: `#14b8a6` (teal)

### Neutral Colors
- Dark: `#1e293b`
- Medium: `#64748b`
- Light: `#94a3b8`
- Default: `currentColor` (inherits from parent)

## Examples

### Success Badge
```razor
<span class="px-2 py-1 bg-green-100 text-green-700 rounded">
    <Icon Name="check" Size="16" Color="#10b981" /> Active
</span>
```

### Action Button
```razor
<button class="btn btn-primary">
    <Icon Name="plus" Size="18" Color="white" /> Add New
</button>
```

### Status Indicator
```razor
@if (isConnected)
{
    <Icon Name="power" Color="#10b981" />
}
else
{
    <Icon Name="power" Color="#ef4444" />
}
```

### Info Alert
```razor
<div class="alert alert-info">
    <Icon Name="info" Size="20" Color="#3b82f6" />
    <span>This is an informational message.</span>
</div>
```

## Technical Details

- **Type:** Inline SVG
- **Default Size:** 20px × 20px
- **Default Color:** `currentColor` (inherits text color)
- **Stroke Width:** 2px
- **Style:** Outline/stroke icons (Lucide icon set style)
- **Accessibility:** Icons are decorative; ensure text labels for screen readers

## Migration Notes

When replacing emojis with the Icon component:

1. Add `@using DocViewer_Proto.Components.UI` to page or `_Imports.razor`
2. Replace emoji Unicode characters with `<Icon Name="..." />`
3. Adjust size as needed (default 20px matches most emoji sizes)
4. Add colors for better visual hierarchy
5. Maintain spacing with existing layout

Example migration:
```razor
<!-- Before -->
<button>🔍 Search</button>

<!-- After -->
<button><Icon Name="search" Size="18" /> Search</button>
```

## Performance

- Icons are rendered inline (no HTTP requests)
- SVG code is minimal (~100-300 bytes per icon)
- No external dependencies
- Scales perfectly at any size
- Works in all modern browsers
