# MyFinder - AI Video Explorer

![MyFinder Dashboard](assets/screenshot.png)

MyFinder is a modern, high-performance video file explorer designed for content creators, forensic analysts, and data hoarders. It leverages local AI to analyze, tag, and organize your video library without sending data to the cloud.

## Key Features

*   **📂 Smart Scanning**: Rapidly indexes terabytes of video content across multiple drives.
*   **👁 AI Object Detection**: Automatically detects **Persons**, **Vehicles** (Cars, Trucks, Bikes), and **Animals** using YOLOv8.
*   **📅 Timestamp Extraction**: Extracts date/time overlays from dashcam and bodycam footage using native Windows OCR.
*   **🔍 Semantic Search**: Find videos by content tags (e.g., "Car", "Person") even if the filenames are generic.
*   **📝 Integrated Transcription**: Transcribe audio tracks locally and jump to specific spoken phrases.
*   **🔒 Privacy First**: All analysis happens on your device. No data leaves your machine.

## Download

[**Download Latest Release (v1.0.0)**](https://github.com/dparksports/myfinder/releases/download/v1.0.0/MyFinder_v1.0.0.zip)

## Installation

1.  Download the `.zip` file from the link above.
2.  Extract the contents to a folder (e.g., `C:\Apps\MyFinder`).
3.  Run `MyFinder.exe`.
4.  (Optional) Install the [Windows App SDK Runtime](https://aka.ms/windowsappsdk/1.2/1.2.221109.1/windowsappruntimeinstall-x64.exe) if prompted.

## Requirements

*   Windows 10 (version 19041) or higher.
*   .NET 8.0 Runtime.
*   (Recommended) Dedicated GPU for faster AI analysis.

## License

Apache License 2.0. See [LICENSE](LICENSE) for details.
