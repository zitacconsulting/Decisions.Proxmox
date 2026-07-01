# Zitac Proxmox Module (Decisions.Proxmox)

> ⚠️ **Important:** Use this module at your own risk. See the **Disclaimer** section below.

## Overview

**Zitac Proxmox Module** is a comprehensive integration module for the Decisions no-code automation platform that enables interaction with Proxmox VE environments. It provides workflow steps to create, manage, and query virtual machines, containers, storage, networks, snapshots, and ISO images through the Proxmox REST API.

## Table of Contents

- [Features](#features)
- [Requirements](#requirements)
- [Installation](#installation)
- [Available Steps](#available-steps)
- [Authentication](#authentication)
- [Configuration Options](#configuration-options)
- [Building from Source](#building-from-source)
- [Troubleshooting](#troubleshooting)
- [License](#license)
- [Disclaimer](#disclaimer)

## Features

This module provides 31 Decisions workflow steps organized under `Integration/Proxmox`:

### Virtual Machine Lifecycle
- `GetAllVMs` - List all virtual machines across nodes, with optional extended info (network, disk, mounted ISOs)
- `GetVMByID` - Get VM details by ID, with optional extended info
- `CreateVM` - Create a new virtual machine with full hardware configuration
- `DeleteVM` - Remove a virtual machine
- `PowerOnVM` - Power on a virtual machine
- `PowerOffVM` - Power off a virtual machine
- `RebootVM` - Restart a virtual machine
- `SuspendVM` - Suspend a virtual machine
- `ResumeVM` - Resume a suspended virtual machine
- `CloneVM` - Clone an existing virtual machine

### Snapshot Management
- `GetSnapshots` - List all snapshots for a VM
- `CreateSnapshot` - Create a VM snapshot
- `DeleteSnapshot` - Delete a VM snapshot
- `RevertSnapshot` - Revert a VM to a snapshot state

### Network Management
- `GetVMNetworks` - List network interfaces configured on a VM
- `AddVMNetwork` - Add a network interface to a VM
- `UpdateVMNetwork` - Change bridge, model, VLAN tag, or firewall on an existing interface
- `RemoveVMNetwork` - Remove a network interface from a VM
- `GetAllNetworkBridges` - List available network bridges on Proxmox nodes

### Disk Management
- `GetVMDisks` - List disks configured on a VM
- `AddVMDisk` - Add a disk to a VM
- `ResizeVMDisk` - Resize an existing VM disk (increase only)
- `RemoveVMDisk` - Remove a disk from a VM, optionally deleting the volume data

### ISO Management
- `GetAllISOs` - List all ISO files available in Proxmox storage
- `MountISO` - Mount an ISO to a VM (auto-selects the first free IDE slot)
- `UnmountISO` - Unmount an ISO from a VM by slot

### Storage
- `GetStorage` - List storage pools available on Proxmox nodes

### Containers (LXC)
- `GetAllContainers` - List all LXC containers
- `PowerOnContainer` - Start a container
- `PowerOffContainer` - Stop a container

### Nodes
- `GetNodes` - List all nodes in the Proxmox cluster

## Requirements

### Platform Requirements
- **Decisions Platform**: Version 9.0 or higher
- **.NET Runtime**: .NET 9.0
- **Proxmox VE**: Version 7.0 or higher

### Proxmox Permissions
The Proxmox user or API token requires appropriate privileges for intended operations:
- `VM.Audit` — read VM configuration and status
- `VM.PowerMgmt` — power on/off/reboot/suspend
- `VM.Config.Disk` — add/remove/resize disks
- `VM.Config.Network` — add/remove/update network interfaces
- `VM.Config.CDROM` — mount/unmount ISO images
- `VM.Allocate` — create and delete VMs
- `VM.Clone` — clone VMs
- `VM.Snapshot` — create, delete, and revert snapshots
- `Datastore.Audit` — list storage and ISO files
- `Sys.Audit` — list nodes and network bridges

> **Note:** For API token authentication, privileges must be explicitly granted via ACL even for `root@pam` tokens, as tokens use privilege separation by default.

## Installation

### Option 1: Install Pre-built Module
1. Download the compiled module (`Zitac.Proxmox.zip`)
2. Log into Decisions Portal
3. Navigate to **System > Administration > Features**
4. Click **Install Module**
5. Upload the module file
6. Restart the Decisions service if prompted

### Option 2: Build and Install
See [Building from Source](#building-from-source) section below.

### Post-Installation
After installation, the Proxmox steps will be available in the Flow Designer under:
```
Integration > Proxmox > [Category]
```

## Available Steps

All 31 steps follow a consistent pattern:
- **Node input is optional** on all VM/container steps — the module auto-discovers the correct node from the VM ID when left blank
- **Connection** inputs (Hostname, Credentials) are grouped in a dedicated section
- **Error** outcome on every step returns a descriptive error message string

## Authentication

Each step has a **Use API Token** property (under Authentication) that switches the Credentials input between two types:

### API Token (recommended)
```
Token ID:     root@pam!mytoken
Token Secret: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
```
Create tokens under **Datacenter > Permissions > API Tokens** in the Proxmox web UI.

### Username / Password
```
Username: root@pam
Password: yourpassword
```

## Configuration Options

### Ignore SSL Errors
Available on all steps. Enable for environments with self-signed certificates. Not recommended for production.

### Fetch Extended Info (`GetAllVMs`, `GetVMByID`)
When enabled, makes an additional `/config` API call per VM to populate:
- `NetworkInterfaces` — bridge, model, MAC address, VLAN tag, firewall flag
- `Disks` — storage pool, volume name, size
- `MountedISOs` — slot, datastore, filename

Returns `ProxmoxVMExtended` instead of `ProxmoxVM` when enabled.

### Delete Disk Data (`RemoveVMDisk`)
When enabled, also deletes the underlying volume from storage after removing it from the VM config. Requires `Storage` and `Volume Name` inputs in addition to `Interface`.

### Disk Bus (`AddVMDisk`)
Property to select the bus type for new disks: `scsi` (default), `virtio`, `ide`, or `sata`.

## Building from Source

### Prerequisites
- .NET 9.0 SDK
- `CreateDecisionsModule-GlobalTool` dotnet global tool

### Build Steps

```bash
# 1. Build the project
dotnet build Zitac.Proxmox/Zitac.Proxmox.Steps.csproj -c Release

# 2. Install/update the packaging tool
dotnet tool update --global CreateDecisionsModule-GlobalTool

# 3. Create the module package
CreateDecisionsModule -buildmodule Zitac.Proxmox -output "." -buildfile Module.Build.json
```

### Build Output
The build process creates a `Zitac.Proxmox.zip` file in the root directory containing:
- Compiled module DLL (`Zitac.Proxmox.Steps.dll`)
- Module icon (`proxmox.svg`)
- Module metadata (`Module.json`)

This ZIP file can be uploaded directly to Decisions via **System > Administration > Features**.

## Troubleshooting

### SSL Certificate Errors
**Problem:** `The remote certificate is invalid according to the validation procedure`

**Solution:**
- Enable **Ignore SSL Errors** on the step (development only)
- Install a valid SSL certificate on the Proxmox host (production)

### API Token Authorization Errors
**Problem:** `403 Forbidden` or empty results despite valid credentials

**Solution:**
- Proxmox API tokens use privilege separation — permissions must be explicitly granted even for `root@pam` tokens
- Go to **Datacenter > Permissions > API Tokens** and add an ACL entry for `/` with the `Administrator` role (or a more restricted role matching your use case)

### Node Not Found
**Problem:** `VM {id} not found on any node`

**Solution:**
- Verify the VM ID is correct
- Ensure the API token/user has `VM.Audit` privilege on the target nodes
- Check that the VM is not in a stopped state that makes it invisible to the API

### Disk Resize Fails
**Problem:** Error when resizing a disk

**Solution:**
- Proxmox only supports increasing disk size, not decreasing
- Ensure the target storage has sufficient free space
- Detach and re-attach snapshots if the VM has snapshots (Proxmox may block resize with snapshots present)

### ISO Mount Fails
**Problem:** `unable to parse directory volume name`

**Solution:**
- The `FileName` field should be just the filename (e.g. `ubuntu-22.04.iso`), not the full path
- The `Datastore` field should be the storage name (e.g. `local`), not the full volume reference
- The module automatically prepends the `iso/` directory path

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.

Copyright (c) 2024-2026 Zitac Consulting AB

## Disclaimer

This module is provided "as is" without warranties of any kind. Use it at your own risk. The authors, maintainers, and contributors disclaim all liability for any direct, indirect, incidental, special, or consequential damages, including data loss or service interruption, arising from the use of this software.

**Important Notes:**
- Always test in a non-production environment first
- Ensure proper backups before performing destructive operations (DeleteVM, RemoveVMDisk with Delete Disk Data enabled)
- Review Proxmox VE documentation for best practices
- This module is not officially supported by Proxmox Server Solutions GmbH or Decisions
