# System Design: MyFinder (AI-Powered Media Explorer)

## 1. Executive Summary
MyFinder is a local, privacy-first file explorer designed for massive media collections. It leverages local AI to index, analyze, and organize video and audio files across multiple drives. It focuses on identifying content value (low-content filtering), deduplication, and semantic retrieval (Face, Voice, Text search).

**Feasibility Verdict**: **YES, it is doable**, but it requires a specialized architecture to handle performance constraints. It functions less like a standard file explorer (instant view) and more like a **Digital Asset Management (DAM)** system that ingests and indexes content in the background.

---

## 2. High-Level Architecture (Revised: C# Native)
We will use a **Pure C# / .NET 8** stack. This simplifies deployment (single .exe) and ensures deep Windows integration.

### A. Frontend (UI)
*   **Tech Stack**: **WinUI 3 (Windows App SDK)**.
*   **Reasoning**: Modern, native Windows 11 look and feel, high performance, and excellent virtualization for large lists (File Explorer-style).

### B. Backend Engine (The "Brain")
*   **Tech Stack**: **C# (.NET 8)**.
*   **AI Runtime**: **ONNX Runtime** & **ML.NET**.
    *   Instead of Python, we use the `.onnx` versions of state-of-the-art models.
    *   **Transcription**: **Whisper.net** (C# wrapper for high-performance `whisper.cpp`).

### C. Data Layer (Simplified)
*   **Storage**: **JSON Files**.
    *   `index_master.json`: Maps file paths to IDs.
    *   `metadata_shard_{N}.json`: Stores analysis data to avoid one massive file lock.
    *   `embeddings.bin`: A custom binary file to store float arrays (vectors) for fast loading, as JSON is too slow for millions of floats.

---

## 3. C# "Pure" AI Implementation Strategy

### 1. Multi-Drive & Change Tracking
*   **Library**: `System.IO.FileSystemWatcher` is native and robust.
*   **Concurrency**: usage of `System.Threading.Channels` to queue file events (Created, Deleted) so the UI doesn't freeze.

### 2. "Low Content" Identification (Object Detection)
*   **Tool**: **YOLOv8 via ONNX Runtime**.
*   **Implementation**:
    *   Use `OpenCVSharp` to grab frames.
    *   Pass tensors to `Microsoft.ML.OnnxRuntime`.
    *   Output: Bounding boxes. If a video has 0 boxes of interest for 90% duration -> Flag "Low Content".

### 3. Transcription (Speech-to-Text)
*   **Tool**: **Whisper.net** (runs on CPU/GPU via DirectCompute/CUDA).
*   **Performance**: Extremely fast in C# (native C++ interop).
*   **Output**: Searchable text segments with timestamps.

### 4. Face Recognition
*   **Pipeline (C#)**:
    1.  **Detection**: `UltraFace` (ONNX) or `scrfd`.
    2.  **Recognition**: `ArcFace` (ONNX).
    3.  **Clustering**: Custom implementation of DBSCAN in C# or using `ML.NET` clustering.

### 5. Voice Fingerprinting
*   **Challenge**: This is the hardest part in C#.
*   **Solution**: Run a **VoxCeleb** pre-trained ONNX model.
    *   Extract audio clip -> MFCC/Spectrogram (via `NAudio`) -> ONNX Model -> 512d Vector.
    *   Compare vectors using `TensorPrimitives.CosineSimilarity` (new in .NET 8, extremely fast).

---

## 4. Feasibility & Trade-offs (User Request: No DB, Pure C#)

### **Is it doable?**
**YES.**
*   **Pros**: Single executable, easy to distribute, no Python environment issues, lower RAM usage (usually).
*   **Cons (JSON Storage)**:
    *   **Searching**: We have to load *everything* into memory to search quickly since we don't have SQL indices.
    *   **Scalability**: If you have >100,000 files, startup time might take a few seconds to deserialize the JSONs.
    *   **Mitigation**: We will use "Lazy Loading" (load JSONs only when searching or browsing that drive).

### **Hardware**
ONNX Runtime supports **DirectML**, meaning it works great on AMD, Intel, and NVIDIA GPUs natively on Windows without complex CUDA setups.


## 5. Timeline / Roadmap
1.  **Prototype**: Basic file scanning + SQLite DB + Video Playback UI.
2.  **AI Integration 1**: Content-Analysis (YOLO) & Deduplication.
3.  **AI Integration 2**: Transcription (Whisper).
## 6. Voice ID Strategy for Sparse Dialogs (Large Files)

### **Challenge**
Large video files (e.g., 2GB, 1 hour) often have sparse dialogue. Loading the entire file into memory crashes the app. Sampling only the first 3 seconds often misses the voice Entirely.

### **Solution Design**
Instead of a single "Voice ID" check, we implement a **Voice Scanning Pipeline**:

#### **Phase 1: Efficient Scanning (Stream-based)**
1.  **Audio Extraction**: Extract the audio track to a temporary, low-bitrate (16kHz mono) WAV file.
    *   *Why?* Reading a 50MB audio file is much faster/ safer than seeking through a 2GB video file.
2.  **Voice Activity Detection (VAD)**:
    *   **Option A (High Accuracy)**: Use **Whisper** to transcribe the file. Whisper implicitly performs VAD. We get a list of segments: `[{Start: 10s, End: 15s}, {Start: 500s, End: 505s}]`.
    *   **Option B (Fast)**: Use simple RMS/Energy gating (if volume > threshold).

#### **Phase 2: Embedding Extraction**
Iterate through the detected segments:
1.  **Snippet Extraction**: Read the specific audio chunk (e.g., `10s` to `15s`) from the temp WAV.
2.  **Vectorization**: Pass the chunk to the `VoiceFingerprintService` (VoxCeleb ONNX).
3.  **Storage**: Store `{ FileID, TimeStart, TimeEnd, Vector[] }`.

#### **Phase 3: Clustering (Diarization)**
1.  **Compare**: Calculate Cosine Similarity between all vectors in the file.
2.  **Group**: Cluster similar vectors together (e.g., "Speaker A", "Speaker B").
3.  **UI Representation**:
    *   Instead of "File has Voice X", show a **Timeline** or **Speaker List**.
    *   **User Action**: "Found 3 distinct voices. Click to listen to samples." -> User names Speaker A "John".

### **Implementation Roadmap**
1.  **Update `VoiceFingerprintService`**: Add method `ProcessAudioSegments(string audioPath, List<Segment> segments)`.
2.  **Update UI**: Add a "Voice Analysis" tab or detailed view that lists detected speakers and their timestamps.
