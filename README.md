# SteamCLI
SteamCLI is a client for the [BytexDigital.Steam](https://github.com/BytexDigital/BytexDigital.Steam) .NET Core library.
With Steam CLI you can download Steam apps and workshop items.

### Parameters
```
  --username                Required. Username to use when logging into Steam.

  --password                Required. Password to use when logging into Steam.

  --targetdir               Specifies a directory to perform an action in.

  --branch                  Specifies a product branch.

  --branchpassword          Specifies a product banch password.

  --os                      Specifies an operating system.

  --workers                 (Default: 50) Specifies how many download workers should work on one download task at a
                            time.

  --buffersize              (Default: 3221225472) Specifies how big the buffer for downloaded chunks is (in bytes)
                            before being written to disk.

  --bufferusagethreshold    (Default: 1) Specifies how empty the chunk buffer has to be before writing to it again.

  --appid                   Specifies an app ID to use.

  --depotid                 Specifies an depot ID to use.

  --synctarget              If enabled, only changed files in the target directory will be downloaded (only changed
                            files will be deleted before download).

  --enablesyncdelete        If enabled, synctarget will also delete files that are not in the original download, making
                            the download target folder a 1 to 1 copy of the original.

  --manifestid              Specifies an manifest ID to use.

  --workshop-download       (Default: false) If set, downloads a workshop item.

  --workshopid              Specifies a workshop item ID to use.

  --app-download            (Default: false) If set, downloads an application.

  --help                    Display this help screen.

  --version                 Display version information.
```

### Examples
#### Downloading a workshop item
```
.\steamcli.exe --username=user --password=passwd --workshop-download --targetdir=.\download --os=windows --appid=107410 --workshopid=549676314 --synctarget
```

#### Downloading an application
```
.\steamcli.exe --username=user --password=passwd --app-download --targetdir=.\download --os=windows --appid=107410 --depotid=107419 --synctarget
```

##### Option `--synctarget`
If this option is enabled, the target directory will be scanned for existing files that are included in the download. If a file is already up to date, it's skipped, otherwise it's replaced with the downloaded version.

##### Option `--enablesyncdelete`
By default, `--synctarget` only adds and replaces files, but does not delete any. If this option is enabled, all files in the target directory that are NOT in the to be downloaded version are deleted.
