// EdsMedia: Windows Media Foundation H.264/AAC MP4 writer and MP4 reader for EasyDanceStudio
// (H3-02/H3-03, decision 10: Media Foundation H.264 first). Uses only codecs that ship with
// Windows; nothing is bundled. Built with scripts/build-native-media.bat (static CRT, x64).
//
// Writer: video frames are BGRA, bottom-up rows (Unity ReadPixels order), timestamps computed
// from the frame index as exact integers (100 ns units = index * 10^7 * fpsDen / fpsNum).
// Audio is interleaved 16-bit PCM, timestamps from the running sample count.
// Reader: reports container facts (codec, size, frame rate, frame count, durations, first
// timestamps) and decodes the audio track to 16-bit PCM for sync measurement.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mferror.h>
#include <string.h>
#include <stdint.h>

#pragma comment(lib, "mfplat.lib")
#pragma comment(lib, "mfreadwrite.lib")
#pragma comment(lib, "mfuuid.lib")
#pragma comment(lib, "ole32.lib")

#define EXPORT extern "C" __declspec(dllexport)

template <class T> static void SafeRelease(T** p) { if (*p) { (*p)->Release(); *p = nullptr; } }

static LONG g_started = 0;
static HRESULT EnsureStarted()
{
    HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (hr == RPC_E_CHANGED_MODE) hr = S_OK; // the thread already has a COM apartment
    if (InterlockedCompareExchange(&g_started, 1, 0) == 0)
    {
        HRESULT start = MFStartup(MF_VERSION, MFSTARTUP_FULL);
        if (FAILED(start)) { InterlockedExchange(&g_started, 0); return start; }
    }
    return S_OK;
}

struct Writer
{
    IMFSinkWriter* sink = nullptr;
    DWORD video = 0, audio = 0;
    UINT32 width = 0, height = 0;
    UINT64 fpsNum = 0, fpsDen = 1;
    bool hasAudio = false;
    UINT32 sampleRate = 0, channels = 0;
    UINT64 audioFrames = 0;
};

static LONGLONG FrameTime(const Writer* w, UINT64 index) { return (LONGLONG)(index * 10000000ULL * w->fpsDen / w->fpsNum); }

EXPORT HRESULT EdsMf_Open(const wchar_t* path, int width, int height, int fpsNum, int fpsDen, int videoBitrate,
                          int sampleRate, int channels, int audioBytesPerSecond, void** handle)
{
    *handle = nullptr;
    if (width <= 0 || height <= 0 || (width & 1) || (height & 1) || fpsNum <= 0 || fpsDen <= 0) return E_INVALIDARG;
    HRESULT hr = EnsureStarted();
    if (FAILED(hr)) return hr;
    Writer* w = new Writer();
    w->width = (UINT32)width; w->height = (UINT32)height; w->fpsNum = (UINT64)fpsNum; w->fpsDen = (UINT64)fpsDen;
    w->hasAudio = sampleRate > 0 && channels > 0; w->sampleRate = (UINT32)sampleRate; w->channels = (UINT32)channels;
    IMFAttributes* attributes = nullptr;
    IMFMediaType* out = nullptr; IMFMediaType* in = nullptr;
    hr = MFCreateAttributes(&attributes, 3);
    if (SUCCEEDED(hr)) hr = attributes->SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, TRUE);
    if (SUCCEEDED(hr)) hr = attributes->SetGUID(MF_TRANSCODE_CONTAINERTYPE, MFTranscodeContainerType_MPEG4);
    if (SUCCEEDED(hr)) hr = MFCreateSinkWriterFromURL(path, nullptr, attributes, &w->sink);
    // Video: H.264 out, RGB32 (BGRA) in; the sink writer inserts the colour conversion.
    if (SUCCEEDED(hr)) hr = MFCreateMediaType(&out);
    if (SUCCEEDED(hr)) hr = out->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    if (SUCCEEDED(hr)) hr = out->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264);
    if (SUCCEEDED(hr)) hr = out->SetUINT32(MF_MT_AVG_BITRATE, (UINT32)videoBitrate);
    if (SUCCEEDED(hr)) hr = out->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
    if (SUCCEEDED(hr)) hr = MFSetAttributeSize(out, MF_MT_FRAME_SIZE, w->width, w->height);
    if (SUCCEEDED(hr)) hr = MFSetAttributeRatio(out, MF_MT_FRAME_RATE, (UINT32)fpsNum, (UINT32)fpsDen);
    if (SUCCEEDED(hr)) hr = MFSetAttributeRatio(out, MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
    if (SUCCEEDED(hr)) hr = w->sink->AddStream(out, &w->video);
    if (SUCCEEDED(hr)) hr = MFCreateMediaType(&in);
    if (SUCCEEDED(hr)) hr = in->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    if (SUCCEEDED(hr)) hr = in->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32);
    if (SUCCEEDED(hr)) hr = in->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
    if (SUCCEEDED(hr)) hr = MFSetAttributeSize(in, MF_MT_FRAME_SIZE, w->width, w->height);
    if (SUCCEEDED(hr)) hr = MFSetAttributeRatio(in, MF_MT_FRAME_RATE, (UINT32)fpsNum, (UINT32)fpsDen);
    if (SUCCEEDED(hr)) hr = MFSetAttributeRatio(in, MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
    // Top-down rows (WriteVideo flips Unity's bottom-up readback; a negative default stride was
    // ignored by the conversion path and produced upside-down video).
    if (SUCCEEDED(hr)) hr = in->SetUINT32(MF_MT_DEFAULT_STRIDE, w->width * 4);
    if (SUCCEEDED(hr)) hr = w->sink->SetInputMediaType(w->video, in, nullptr);
    SafeRelease(&out); SafeRelease(&in);
    if (SUCCEEDED(hr) && w->hasAudio)
    {
        hr = MFCreateMediaType(&out);
        if (SUCCEEDED(hr)) hr = out->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
        if (SUCCEEDED(hr)) hr = out->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_AAC);
        if (SUCCEEDED(hr)) hr = out->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
        if (SUCCEEDED(hr)) hr = out->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, w->sampleRate);
        if (SUCCEEDED(hr)) hr = out->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, w->channels);
        if (SUCCEEDED(hr)) hr = out->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, (UINT32)audioBytesPerSecond);
        if (SUCCEEDED(hr)) hr = w->sink->AddStream(out, &w->audio);
        if (SUCCEEDED(hr)) hr = MFCreateMediaType(&in);
        if (SUCCEEDED(hr)) hr = in->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
        if (SUCCEEDED(hr)) hr = in->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_PCM);
        if (SUCCEEDED(hr)) hr = in->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
        if (SUCCEEDED(hr)) hr = in->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, w->sampleRate);
        if (SUCCEEDED(hr)) hr = in->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, w->channels);
        if (SUCCEEDED(hr)) hr = in->SetUINT32(MF_MT_AUDIO_BLOCK_ALIGNMENT, w->channels * 2);
        if (SUCCEEDED(hr)) hr = in->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, w->sampleRate * w->channels * 2);
        if (SUCCEEDED(hr)) hr = w->sink->SetInputMediaType(w->audio, in, nullptr);
        SafeRelease(&out); SafeRelease(&in);
    }
    if (SUCCEEDED(hr)) hr = w->sink->BeginWriting();
    SafeRelease(&attributes);
    if (FAILED(hr)) { SafeRelease(&w->sink); delete w; return hr; }
    *handle = w;
    return S_OK;
}

static HRESULT WriteBuffer(IMFSinkWriter* sink, DWORD stream, const BYTE* data, DWORD bytes, LONGLONG time, LONGLONG duration)
{
    IMFMediaBuffer* buffer = nullptr; IMFSample* sample = nullptr; BYTE* target = nullptr;
    HRESULT hr = MFCreateMemoryBuffer(bytes, &buffer);
    if (SUCCEEDED(hr)) hr = buffer->Lock(&target, nullptr, nullptr);
    if (SUCCEEDED(hr)) { memcpy(target, data, bytes); hr = buffer->Unlock(); }
    if (SUCCEEDED(hr)) hr = buffer->SetCurrentLength(bytes);
    if (SUCCEEDED(hr)) hr = MFCreateSample(&sample);
    if (SUCCEEDED(hr)) hr = sample->AddBuffer(buffer);
    if (SUCCEEDED(hr)) hr = sample->SetSampleTime(time);
    if (SUCCEEDED(hr)) hr = sample->SetSampleDuration(duration);
    if (SUCCEEDED(hr)) hr = sink->WriteSample(stream, sample);
    SafeRelease(&sample); SafeRelease(&buffer);
    return hr;
}

// bgra: width x height x 4 bytes, bottom-up rows.
EXPORT HRESULT EdsMf_WriteVideo(void* handle, const BYTE* bgra, long long frameIndex)
{
    Writer* w = (Writer*)handle;
    if (!w || !bgra || frameIndex < 0) return E_INVALIDARG;
    LONGLONG time = FrameTime(w, (UINT64)frameIndex);
    DWORD row = w->width * 4, bytes = row * w->height;
    IMFMediaBuffer* buffer = nullptr; IMFSample* sample = nullptr; BYTE* target = nullptr;
    HRESULT hr = MFCreateMemoryBuffer(bytes, &buffer);
    if (SUCCEEDED(hr)) hr = buffer->Lock(&target, nullptr, nullptr);
    if (SUCCEEDED(hr))
    {
        for (UINT32 y = 0; y < w->height; y++) memcpy(target + (size_t)y * row, bgra + (size_t)(w->height - 1 - y) * row, row);
        hr = buffer->Unlock();
    }
    if (SUCCEEDED(hr)) hr = buffer->SetCurrentLength(bytes);
    if (SUCCEEDED(hr)) hr = MFCreateSample(&sample);
    if (SUCCEEDED(hr)) hr = sample->AddBuffer(buffer);
    if (SUCCEEDED(hr)) hr = sample->SetSampleTime(time);
    if (SUCCEEDED(hr)) hr = sample->SetSampleDuration(FrameTime(w, (UINT64)frameIndex + 1) - time);
    if (SUCCEEDED(hr)) hr = w->sink->WriteSample(w->video, sample);
    SafeRelease(&sample); SafeRelease(&buffer);
    return hr;
}

// pcm: frames x channels interleaved 16-bit samples, appended after the previous call.
EXPORT HRESULT EdsMf_WriteAudio(void* handle, const short* pcm, int frames)
{
    Writer* w = (Writer*)handle;
    if (!w || !w->hasAudio || !pcm || frames <= 0) return E_INVALIDARG;
    LONGLONG time = (LONGLONG)(w->audioFrames * 10000000ULL / w->sampleRate);
    LONGLONG end = (LONGLONG)((w->audioFrames + (UINT64)frames) * 10000000ULL / w->sampleRate);
    HRESULT hr = WriteBuffer(w->sink, w->audio, (const BYTE*)pcm, (DWORD)frames * w->channels * 2, time, end - time);
    if (SUCCEEDED(hr)) w->audioFrames += (UINT64)frames;
    return hr;
}

EXPORT HRESULT EdsMf_Finish(void* handle)
{
    Writer* w = (Writer*)handle;
    if (!w) return E_INVALIDARG;
    HRESULT hr = w->sink->Finalize();
    SafeRelease(&w->sink);
    delete w;
    return hr;
}

// Cancel/failure: release without finalizing (the caller deletes the temporary file).
EXPORT void EdsMf_Abort(void* handle)
{
    Writer* w = (Writer*)handle;
    if (!w) return;
    SafeRelease(&w->sink);
    delete w;
}

// ----- Reader -----
#pragma pack(push, 8)
struct ProbeInfo
{
    UINT32 videoCodec;      // FOURCC (e.g. 'H264' = 0x34363248)
    UINT32 width, height;
    UINT32 fpsNum, fpsDen;
    long long videoFrames;
    long long videoDuration;    // 100 ns, from the last sample time + duration
    long long firstVideoTime;
    UINT32 audioCodec;      // FOURCC/format tag (AAC = 0x1610)
    UINT32 sampleRate, channels;
    long long audioDuration;
    long long firstAudioTime;
    long long containerDuration; // MF_PD_DURATION
};
#pragma pack(pop)

static HRESULT OpenReader(const wchar_t* path, IMFSourceReader** reader)
{
    HRESULT hr = EnsureStarted();
    if (FAILED(hr)) return hr;
    return MFCreateSourceReaderFromURL(path, nullptr, reader);
}

EXPORT HRESULT EdsMf_Probe(const wchar_t* path, ProbeInfo* info)
{
    memset(info, 0, sizeof(*info));
    IMFSourceReader* reader = nullptr;
    HRESULT hr = OpenReader(path, &reader);
    if (FAILED(hr)) return hr;
    PROPVARIANT duration; PropVariantInit(&duration);
    if (SUCCEEDED(reader->GetPresentationAttribute((DWORD)MF_SOURCE_READER_MEDIASOURCE, MF_PD_DURATION, &duration)) && duration.vt == VT_UI8)
        info->containerDuration = (long long)duration.uhVal.QuadPart;
    PropVariantClear(&duration);
    IMFMediaType* type = nullptr; GUID subtype;
    if (SUCCEEDED(reader->GetNativeMediaType((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, 0, &type)))
    {
        if (SUCCEEDED(type->GetGUID(MF_MT_SUBTYPE, &subtype))) info->videoCodec = subtype.Data1;
        MFGetAttributeSize(type, MF_MT_FRAME_SIZE, &info->width, &info->height);
        MFGetAttributeRatio(type, MF_MT_FRAME_RATE, &info->fpsNum, &info->fpsDen);
        SafeRelease(&type);
    }
    if (SUCCEEDED(reader->GetNativeMediaType((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM, 0, &type)))
    {
        if (SUCCEEDED(type->GetGUID(MF_MT_SUBTYPE, &subtype))) info->audioCodec = subtype.Data1;
        type->GetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, &info->sampleRate);
        type->GetUINT32(MF_MT_AUDIO_NUM_CHANNELS, &info->channels);
        SafeRelease(&type);
    }
    // Count the compressed video samples (no decoding) and note the timestamps.
    reader->SetStreamSelection((DWORD)MF_SOURCE_READER_ALL_STREAMS, FALSE);
    if (info->videoCodec)
    {
        reader->SetStreamSelection((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, TRUE);
        info->firstVideoTime = -1;
        for (;;)
        {
            DWORD flags = 0; LONGLONG time = 0; IMFSample* sample = nullptr;
            hr = reader->ReadSample((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, 0, nullptr, &flags, &time, &sample);
            if (FAILED(hr) || (flags & MF_SOURCE_READERF_ENDOFSTREAM)) { SafeRelease(&sample); break; }
            if (sample)
            {
                LONGLONG length = 0; sample->GetSampleDuration(&length);
                if (info->firstVideoTime < 0 || time < info->firstVideoTime) info->firstVideoTime = time;
                if (time + length > info->videoDuration) info->videoDuration = time + length;
                info->videoFrames++;
                SafeRelease(&sample);
            }
        }
        reader->SetStreamSelection((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, FALSE);
    }
    if (info->audioCodec)
    {
        // A fresh reader for the audio pass (rewinding the first one is not reliable).
        SafeRelease(&reader);
        if (FAILED(OpenReader(path, &reader))) return S_OK;
        reader->SetStreamSelection((DWORD)MF_SOURCE_READER_ALL_STREAMS, FALSE);
        reader->SetStreamSelection((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM, TRUE);
        info->firstAudioTime = -1;
        for (;;)
        {
            DWORD flags = 0; LONGLONG time = 0; IMFSample* sample = nullptr;
            hr = reader->ReadSample((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM, 0, nullptr, &flags, &time, &sample);
            if (FAILED(hr) || (flags & MF_SOURCE_READERF_ENDOFSTREAM)) { SafeRelease(&sample); break; }
            if (sample)
            {
                LONGLONG length = 0; sample->GetSampleDuration(&length);
                if (info->firstAudioTime < 0 || time < info->firstAudioTime) info->firstAudioTime = time;
                if (time + length > info->audioDuration) info->audioDuration = time + length;
                SafeRelease(&sample);
            }
        }
    }
    SafeRelease(&reader);
    return S_OK;
}

// Decodes video frame `frameIndex` (0-based, in decode order) to BGRA, bottom-up rows (Unity's
// texture order), width x height x 4 bytes. Used by the tests to compare with the render.
EXPORT HRESULT EdsMf_ReadVideoFrame(const wchar_t* path, int frameIndex, BYTE* bgra, int width, int height)
{
    HRESULT hr = EnsureStarted();
    if (FAILED(hr)) return hr;
    IMFAttributes* attributes = nullptr; IMFSourceReader* reader = nullptr; IMFMediaType* type = nullptr;
    hr = MFCreateAttributes(&attributes, 1);
    if (SUCCEEDED(hr)) hr = attributes->SetUINT32(MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, TRUE);
    if (SUCCEEDED(hr)) hr = MFCreateSourceReaderFromURL(path, attributes, &reader);
    SafeRelease(&attributes);
    if (SUCCEEDED(hr)) hr = MFCreateMediaType(&type);
    if (SUCCEEDED(hr)) hr = type->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    if (SUCCEEDED(hr)) hr = type->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32);
    if (SUCCEEDED(hr)) hr = reader->SetStreamSelection((DWORD)MF_SOURCE_READER_ALL_STREAMS, FALSE);
    if (SUCCEEDED(hr)) hr = reader->SetStreamSelection((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, TRUE);
    if (SUCCEEDED(hr)) hr = reader->SetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, nullptr, type);
    SafeRelease(&type);
    // Row order of the decoded RGB32 buffer: a positive stride is top-down (the default).
    LONG stride = width * 4;
    if (SUCCEEDED(hr) && SUCCEEDED(reader->GetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, &type)))
    {
        UINT32 value = 0;
        if (SUCCEEDED(type->GetUINT32(MF_MT_DEFAULT_STRIDE, &value))) stride = (LONG)(INT32)value;
        SafeRelease(&type);
    }
    int index = 0;
    bool found = false;
    while (SUCCEEDED(hr) && !found)
    {
        DWORD flags = 0; LONGLONG time = 0; IMFSample* sample = nullptr;
        hr = reader->ReadSample((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, 0, nullptr, &flags, &time, &sample);
        if (FAILED(hr)) break;
        if (flags & MF_SOURCE_READERF_ENDOFSTREAM) { SafeRelease(&sample); hr = MF_E_END_OF_STREAM; break; }
        if (!sample) continue;
        if (index++ == frameIndex)
        {
            IMFMediaBuffer* buffer = nullptr; IMF2DBuffer* buffer2d = nullptr;
            hr = sample->GetBufferByIndex(0, &buffer);
            if (SUCCEEDED(hr) && SUCCEEDED(buffer->QueryInterface(IID_PPV_ARGS(&buffer2d))))
            {
                BYTE* scan0 = nullptr; LONG pitch = 0;
                hr = buffer2d->Lock2D(&scan0, &pitch);
                if (SUCCEEDED(hr))
                {
                    // scan0 is the top row; Unity wants the bottom row first.
                    for (int y = 0; y < height; y++) memcpy(bgra + (size_t)(height - 1 - y) * width * 4, scan0 + (ptrdiff_t)y * pitch, (size_t)width * 4);
                    buffer2d->Unlock2D();
                }
                SafeRelease(&buffer2d);
            }
            else if (SUCCEEDED(hr))
            {
                BYTE* data = nullptr; DWORD length = 0;
                hr = buffer->Lock(&data, nullptr, &length);
                if (SUCCEEDED(hr))
                {
                    size_t row = (size_t)width * 4;
                    if (length >= row * height)
                        for (int y = 0; y < height; y++)
                        {
                            // Unity row y (from the bottom) is buffer row height-1-y when top-down.
                            size_t source = stride > 0 ? (size_t)(height - 1 - y) * row : (size_t)y * row;
                            memcpy(bgra + (size_t)y * row, data + source, row);
                        }
                    buffer->Unlock();
                }
            }
            SafeRelease(&buffer);
            found = true;
        }
        SafeRelease(&sample);
    }
    SafeRelease(&reader);
    return found ? hr : (FAILED(hr) ? hr : E_FAIL);
}

// Decodes the audio track to interleaved 16-bit PCM (at most maxFrames frames).
EXPORT HRESULT EdsMf_ReadAudio(const wchar_t* path, short* pcm, int maxFrames, int* frames, int* sampleRate, int* channels)
{
    *frames = 0; *sampleRate = 0; *channels = 0;
    IMFSourceReader* reader = nullptr;
    HRESULT hr = OpenReader(path, &reader);
    if (FAILED(hr)) return hr;
    IMFMediaType* type = nullptr;
    hr = MFCreateMediaType(&type);
    if (SUCCEEDED(hr)) hr = type->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
    if (SUCCEEDED(hr)) hr = type->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_PCM);
    if (SUCCEEDED(hr)) hr = type->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
    if (SUCCEEDED(hr)) hr = reader->SetStreamSelection((DWORD)MF_SOURCE_READER_ALL_STREAMS, FALSE);
    if (SUCCEEDED(hr)) hr = reader->SetStreamSelection((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM, TRUE);
    if (SUCCEEDED(hr)) hr = reader->SetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM, nullptr, type);
    SafeRelease(&type);
    if (SUCCEEDED(hr)) hr = reader->GetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM, &type);
    UINT32 rate = 0, count = 0;
    if (SUCCEEDED(hr)) { type->GetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, &rate); type->GetUINT32(MF_MT_AUDIO_NUM_CHANNELS, &count); SafeRelease(&type); }
    *sampleRate = (int)rate; *channels = (int)count;
    int written = 0;
    while (SUCCEEDED(hr) && count > 0)
    {
        DWORD flags = 0; LONGLONG time = 0; IMFSample* sample = nullptr;
        hr = reader->ReadSample((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM, 0, nullptr, &flags, &time, &sample);
        if (FAILED(hr) || (flags & MF_SOURCE_READERF_ENDOFSTREAM)) { SafeRelease(&sample); break; }
        if (!sample) continue;
        IMFMediaBuffer* buffer = nullptr; BYTE* data = nullptr; DWORD length = 0;
        if (SUCCEEDED(sample->ConvertToContiguousBuffer(&buffer)) && SUCCEEDED(buffer->Lock(&data, nullptr, &length)))
        {
            int available = (int)(length / (count * 2));
            int take = available < maxFrames - written ? available : maxFrames - written;
            if (take > 0) memcpy(pcm + (size_t)written * count, data, (size_t)take * count * 2);
            written += take;
            buffer->Unlock();
        }
        SafeRelease(&buffer); SafeRelease(&sample);
        if (written >= maxFrames) break;
    }
    *frames = written;
    SafeRelease(&reader);
    return hr == MF_E_END_OF_STREAM ? S_OK : (FAILED(hr) ? hr : S_OK);
}
