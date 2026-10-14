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

This module provides 37 Decisions workflow steps organized under `Integration/Proxmox`:

### Virtual Machine Lifecycle
- `GetAllVMs` - List all virtual machines in the cluster (or on one node), with optional extended info (disks, CD-ROMs, NICs, guest agent)
- `GetVMByID` - Get a VM with full details by ID
- `GetVMByName` - Get a VM with full details by name (`Multiple Found` outcome when names collide)
- `CreateVM` - Create a new virtual machine with full hardware configuration
- `DeleteVM` - Remove a virtual machine and all its disks. Waits for the delete task (*Wait For Task Completion*), *Purge From Backup, Replication and HA Jobs* (on by default), *Destroy Unreferenced Disks* (also volumes with the VM's ID that aren't in its config), optional `Not Found` and `Still Running` outcomes (a running VM is never deleted)
- `PowerOnVM` - Power on a virtual machine, optional `Already Running` outcome. *Wait For OS Boot* waits until the QEMU Guest Agent inside the guest answers (`Timeout` outcome after *OS Boot Timeout*); the agent must be enabled on the VM and installed in the guest.
- `PowerOffVM` - Shut down a virtual machine (graceful by default, hard stop with Force Stop), optional `Not Running` outcome
- `RebootVM` - Restart a virtual machine (full shutdown and start, so pending changes are applied). Same *Wait For OS Boot* option as `PowerOnVM`; it only counts an agent answer from after the reboot.
- `SuspendVM` - Suspend a virtual machine
- `ResumeVM` - Resume a suspended virtual machine
- `CloneVM` - Clone an existing virtual machine, optionally changing CPU/machine type or any other setting on the clone

### Guest Execution (QEMU Guest Agent)
- `RunScriptOnVM` - Run a PowerShell, Cmd, Bash or Sh script inside the guest
- `RunCommandOnVM` - Run a program with an argument list (and optional stdin) inside the guest

Both run as **SYSTEM** (Windows) or **root** (Linux) through the QEMU Guest Agent; no guest login is used. They need the agent enabled on the VM and running in the guest. Settings: *Wait For Execution*, *Timeout* (`Timeout` outcome; the process keeps running in the guest), *Capture Output*, *Show Outcome for 'Not Running'* (VM stopped, agent disabled or not responding). Outputs: `PID`, and when waiting `Exit Code`, `Output` (stdout), `Error Output` (stderr), `Output Truncated`.

How scripts are passed: PowerShell with `-EncodedCommand` (about 12,000 characters max), Cmd as a temporary `.cmd` file with `@echo off` run through PowerShell (about 9,000 characters max), Bash/Sh on stdin (no limit).

### Snapshot Management
- `GetSnapshots` - List all snapshots for a VM
- `CreateSnapshot` - Create a VM snapshot. Proxmox only allows letters, digits, `_` and `-` (starting with a letter, max 40 characters), so the name is converted (e.g. `Snapshot_2026-10-14 19:28` → `Snapshot_2026-10-14_19-28`); the `Snapshot Name` output is the name actually used. `DeleteSnapshot` and `RevertSnapshot` convert names the same way
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
- `RemoveVMDisk` - Remove a disk from a VM by `Interface`. With *Delete Disk Data* the volume is deleted too, the way removing an unused disk works in the Proxmox GUI; it is never deleted while still attached. Outputs: `Pending Restart` (the disk stays attached until the VM is stopped and started, so no data was deleted) and `Disk Data Deleted`. `Not Found` outcome. Refuses CD drives, the EFI disk and TPM state

### ISO Management
- `GetAllISOs` - List all ISO files available in Proxmox storage
- `MountISO` - Mount an ISO to a VM; returns a `ProxmoxCDROM`. Optional `Interface` (e.g. `ide2`): replaces the ISO in that CD drive, or creates a CD drive there if the slot is free. Ignored: loads the ISO into the first empty CD drive, otherwise adds a new drive in the first free IDE slot (`No Free Slot` when all four are used).
- `UnmountISO` - Eject the ISO from a CD drive by `Interface`; the empty drive stays and is reused by `MountISO`
- `RemoveCDROM` - Delete a CD drive by `Interface`. `Pending Restart` output: true when Proxmox keeps the removal pending until the VM is stopped and started (running VM); running it again then still returns `Done`

### Storage
- `GetStorage` - List storage pools available on Proxmox nodes

### Containers (LXC)
- `GetAllContainers` - List all LXC containers
- `PowerOnContainer` - Start a container
- `PowerOffContainer` - Stop a container

### Nodes
- `GetNodes` - List all nodes in the Proxmox cluster
- `GetCPUTypes` - List the CPU types a node offers, including cluster-defined custom models
- `GetMachineTypes` - List the machine types and versions a node's QEMU supports

## Requirements

### Platform Requirements
- **Decisions Platform**: Version 9.31 or higher
- **.NET Runtime**: .NET 10.0
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
- `VM.GuestAgent.Unrestricted` (PVE 9) / `VM.Monitor` (PVE 8) — `RunScriptOnVM` / `RunCommandOnVM`. This is effectively full admin/root inside the guest; grant it only to a dedicated token on the VMs that need it
- `VM.Monitor` (PVE 8) / `VM.GuestAgent.Audit` (PVE 9) — read guest agent info (hostname, OS, IP addresses). Optional; without it `GuestAgent.Responding` is false

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

All 37 steps follow a consistent pattern:
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

### Fetch Extended Info (`GetAllVMs`)
Off (default): one `/cluster/resources` call returns every VM with name, node, power state, template flag, tags, CPUs, CPU usage, memory and uptime. Config fields and the `Disks`/`CDROMs`/`NICs` arrays are empty.

On: each VM is also loaded in full like `GetVMByID`, which costs three or more API calls per VM. VMs on an offline node keep the summary.

### Exclude Templates (`GetAllVMs`)
Leaves VM templates out of the result.

## VM Data Type (`ProxmoxVM`)
Returned by `GetVMByID`, `GetVMByName`, `GetAllVMs` and `CreateVM`. Field names carry their unit (`MemoryMB`, `SizeGB`, `UptimeSeconds`, `CPUUsagePercent`).

| Field | Description |
|---|---|
| `VMID`, `Name`, `Node`, `Notes`, `Tags[]` | Identity and description |
| `PowerState`, `PowerStateDetail` | Enum: `Unknown` (node offline), `Running`, `Stopped`, `Paused` (Suspend VM), `Hibernated` (stopped with a `suspended` lock), `Starting`, `Migrating`, `Error` (I/O error, guest panic, watchdog), `Other`. `PowerStateDetail` is the raw Proxmox/QEMU state. Get All VMs without extended info only sees running/stopped, so a paused VM shows as `Running` there. |
| `Template`, `StartOnBoot` | Flags |
| `Locked`, `LockedBy`, `LockedByDetail` | `Locked` is true while an operation holds the VM and config changes are refused. `LockedBy` enum: `None`, `Backup`, `Clone`, `Create`, `Migrate`, `Snapshot`, `SnapshotDelete`, `Rollback`, `Hibernating`, `Hibernated`, `Other`. `LockedByDetail` is the raw Proxmox lock name. |
| `OSType`, `BIOS`, `MachineType`, `UUID` | Proxmox `ostype` code (`l26`, `win11`, ...), `seabios`/`ovmf`, machine type, SMBIOS UUID |
| `SCSIController` | Controller model for all SCSI disks: `VirtIOScsiPci`, `VirtIOScsiSingle`, `LSI` (Proxmox default), `LSI53C810`, `MegaRAID`, `ParaVirtual`. Proxmox has one per VM; disks aren't attached to a controller individually. |
| `UEFI`, `TPMEnabled` | `UEFI` = BIOS is `ovmf`. `TPMEnabled` = the VM has a virtual TPM (`tpmstate0`). |
| `CPUSockets`, `CPUCores`, `CPUs`, `CPUType`, `CPUUsagePercent` | `CPUs` is the total vCPU count |
| `MemoryMB`, `MemoryUsedMB`, `BalloonMinMB` | Configured, currently used, and balloon minimum (0 = ballooning off) |
| `UptimeSeconds` | |
| `GuestAgent` | `Enabled`, `Responding`, `OSFullName`, `OSVersion`, `KernelRelease`, `Hostname`, `IPAddresses[]` (queried only for running VMs with the agent enabled) |
| `Disks[]` (`ProxmoxDisk`) | Data disks only (the EFI disk and TPM state are left out). `Interface`, `Bus` (scsi / virtio / sata / ide), `Storage`, `VolumeName`, `SizeGB`, `Format`, `Cache`, `SSD`, `Discard`, `IOThread`, `Backup` |
| `CDROMs[]` (`ProxmoxCDROM`) | `Interface`, `Type` (ISO / Physical / Empty), `Storage`, `FileName`, `VolumeID` |
| `NICs[]` (`ProxmoxNetworkInterface`) | `Interface`, `Model`, `MacAddress`, `Bridge`, `VlanTag`, `Firewall`, `Connected`, `MTU`, `RateLimitMBps`, `Queues`, `IPAddresses[]` (from the guest agent, matched on MAC), `OtherOptions` |

`ProxmoxContainer` (from `GetAllContainers`) uses the same naming: `PowerState` (same enum: Running, Stopped or Unknown), `PowerStateDetail`, `CPUs`, `CPUUsagePercent`, `MemoryMB`, `MemoryUsedMB`, `DiskSizeGB`, `DiskUsedGB`, `UptimeSeconds`, `Template`, `Tags[]`, `Locked`, `LockedBy`, `LockedByDetail` (container-only locks such as `disk`, `fstrim` or `mounted` show as `Other`).

### CPU, Memory, Machine Type and Extra Config (`CreateVM`, `CloneVM`)
On **Create VM**, `CPU` and `Memory` start as *Build Data* with `Sockets` = 1, `Cores` = 1 and `MemoryMB` = 2048 (the Proxmox GUI defaults) and every other field ignored. Those three fields and the two inputs themselves are required: the designer flags the step if they are ignored or null, and the step goes to *Error* if they are empty or 0 at runtime. On **Clone VM** everything is optional and ignored by default.

**CPU** (`ProxmoxCPUSettings`), matching the Processor dialog in the Proxmox GUI:

| Field | Notes |
|---|---|
| `Sockets`, `Cores` | |
| `Type` | Dropdown: `Default`, x86-64-v2-AES, x86-64-v2/v3/v4, host, max, kvm64, qemu64 |
| `CustomType` | Any other model, e.g. `EPYC-Genoa` or `custom-mymodel`. Overrides `Type`. |
| `Flags` | Extra CPU flags, e.g. `+aes;-pcid` |
| `VCPUs` | vCPUs active at boot when fewer than Sockets x Cores (hotplug) |
| `LimitCores` | Max CPU time in whole cores, e.g. `2.5`. `0` = unlimited. |
| `CPUUnits` | Scheduler weight relative to other VMs |
| `Affinity` | Host cores the VM may run on, e.g. `0,5,8-11` |
| `NUMA` | |

**Memory** (`ProxmoxMemorySettings`), matching the Memory dialog in the Proxmox GUI:

| Field | Notes |
|---|---|
| `MemoryMB` | |
| `BalloonMinMB` | Minimum memory with ballooning. `0` = ballooning off. |
| `Shares` | Auto-ballooning weight relative to other VMs (default 1000). `0` = no auto-ballooning. |

**Machine Type** (dropdown `I440fx` / `Q35`) and **Machine Version** (text): e.g. Q35 + `11.0` gives `pc-q35-11.0`. Blank version = newest on the node. A full id from `GetMachineTypes` (`pc-q35-10.0+pve1`) is used as-is. A version without a type means i440fx.

**Extra Config** (key/value list): any parameter from the [Proxmox API](https://pve.proxmox.com/pve-docs/api-viewer/) (`nodes/{node}/qemu`), e.g. `numa`=`1`, `ciuser`=`admin`. Applied last, so it overrides the step's own values. Proxmox rejects unknown keys and bad values with an error.

Unset inputs and fields mean:
- **Create VM**: Proxmox defaults (ballooning on, i440fx) and CPU type `x86-64-v2-AES`, the same default the Proxmox GUI uses. The API on its own would fall back to `kvm64`, so the step sends it explicitly.
- **Clone VM**: the clone keeps the source VM's values. Changing `Type`/`CustomType`/`Flags` keeps the source's other CPU options. When anything is set, the step waits for the clone to finish (even if *Wait For Task Completion* is off) and then updates the clone's config.

The available CPU models and machine versions depend on the node's Proxmox/QEMU version, so the dropdowns only hold the stable values. For an up-to-date list (for example to fill a dropdown in a Form), use `GetCPUTypes` / `GetMachineTypes` at runtime and pass the chosen `Name` / `ID` to `CustomType` / `Machine Version`.

### Delete Disk Data (`RemoveVMDisk`)
When enabled, also deletes the disk's volume from storage once it is detached. The volume is read from the VM's config, so no Storage or Volume Name input is needed. If the removal is pending (running VM, disk not hot-removable), nothing is deleted and `Disk Data Deleted` is false. After the VM has been stopped and started, the disk is listed in the VM's config as `unused0`, `unused1`, …; run the step again with that as `Interface` to delete the data. Simplest: power off the VM before removing disks with data.

### Disk Bus (`AddVMDisk`)
Property to select the bus type for new disks: `scsi` (default), `virtio`, `ide`, or `sata`.

## Building from Source

### Prerequisites
- .NET 10.0 SDK
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

### New CD Drive Doesn't Show Up in a Running VM
IDE/SATA drives can't be added to or removed from a running VM. Proxmox saves the change as *pending* and applies it at the next shutdown and start (a reboot from inside the guest is not enough). Loading an ISO into an **existing** CD drive (`MountISO` reusing an empty drive, or with `Interface` set to an existing drive) and `UnmountISO` work immediately.

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
