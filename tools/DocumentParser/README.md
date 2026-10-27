# Bundled read-only document parser

Folder Splitter ships Kordoc 4.24.8, PDF.js 4.10.38 and Node 24.14.0 for Windows
x64 in the application's `parsers/` directory. End users do not install Node,
Office, Hancom, npm, personal agent skills or another document application.
`stage.ps1 -Destination <publish-directory>/parsers` prepares this directory.
The release archive must include the directory next to FolderSplitter.exe.

The host starts its own Node executable directly, never PATH or a shell command.
Protocol 2 reads one document per owned process, with a 45-second deadline,
256 MB JavaScript heap limit and 2 MB protocol response limit. The heap setting
is not a hard total-RSS cap. The default host input cap is 20 MB; each read sends
at most 12 excerpts and 12,000 characters onward. A request reads at most eight
documents and 48,000 characters, without saving a permanent body index.

OCR, formula OCR, image extraction and network model downloads are disabled.
Scanned/image-only files report that no text layer could be read. PDF pages,
slides and sheet locators are distinguished; HWP section approximations are not
presented as physical page numbers. DOCX text without pagination is labeled as
document text.

`KORDOC_OFFLINE=1` is set for every worker. User files are only read. A partial
read or unsupported format must not be presented as an exhaustive no-match.

Redistribution notices remain with every package under `node_modules`, including
Kordoc's MIT LICENSE and NOTICE, PDF.js's Apache-2.0 license, and transitive
dependency licenses. Node's license and third-party notices are `node-LICENSE`.
Do not trim license files when building an installer.

Build-time requirements are .NET SDK, Node/npm and PowerShell; they are not
end-user requirements. The Node archive SHA-256 is verified against the official
release checksum file. Dependency versions are locked by package-lock.json.
