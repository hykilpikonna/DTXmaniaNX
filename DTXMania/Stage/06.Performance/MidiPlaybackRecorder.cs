using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDK;

namespace DTXMania;

public static class MidiPlaybackRecorder
{
    public readonly struct MidiEvent
    {
        public readonly int Note;
        public readonly int Velocity;
        public readonly long TimeMs;
        public readonly int Channel;

        public MidiEvent(int note, int velocity, long timeMs, int channel)
        {
            Note = note;
            Velocity = velocity;
            TimeMs = timeMs;
            Channel = channel;
        }
    }

    private readonly struct RawMidiMessage
    {
        public readonly long TimeMs;
        public readonly byte Status;
        public readonly byte Data1;
        public readonly byte Data2;
        public readonly bool IsNoteOff;

        public RawMidiMessage(long timeMs, byte status, byte data1, byte data2, bool isNoteOff)
        {
            TimeMs = timeMs;
            Status = status;
            Data1 = data1;
            Data2 = data2;
            IsNoteOff = isNoteOff;
        }
    }

    private static readonly List<MidiEvent> recordedEvents = new(4096);
    private static bool isRecording;

    public static bool HasEvents => recordedEvents.Count > 0;
    public static int EventsCount => recordedEvents.Count;

    public static void Start()
    {
        recordedEvents.Clear();
        isRecording = true;
    }

    public static void Stop()
    {
        isRecording = false;
    }

    public static void Clear()
    {
        isRecording = false;
        recordedEvents.Clear();
    }

    public static void Record(CInputManager? inputManager, long resetAtMs)
    {
        if (!isRecording || inputManager?.listInputDevices == null)
            return;

        try
        {
            var devices = inputManager.listInputDevices;
            for (int i = 0; i < devices.Count; i++)
            {
                var device = devices[i];
                if (device != null && device.eInputDeviceType == EInputDeviceType.MidiIn && device.listInputEvent != null && device.listInputEvent.Count > 0)
                {
                    var events = device.listInputEvent;
                    for (int j = 0; j < events.Count; j++)
                    {
                        STInputEvent ev = events[j];
                        if (ev.b押された)
                        {
                            long time = Math.Max(0, ev.nTimeStamp - resetAtMs);
                            int note = Math.Clamp(ev.nKey, 0, 127);
                            int vel = Math.Clamp(ev.nVelocity, 1, 127);
                            const int channel = 9; // MIDI Channel 10 (0-indexed 9) standard percussion channel
                            recordedEvents.Add(new MidiEvent(note, vel, time, channel));
                        }
                    }
                }
            }
        }
        catch
        {
            // Do not disrupt critical input path or gameplay loop
        }
    }

    public static void SaveToFile(string filePath)
    {
        if (recordedEvents.Count == 0)
            return;

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Sort note-ons chronologically
        var sortedNotes = recordedEvents.OrderBy(e => e.TimeMs).ToList();

        // Build list of raw MIDI messages (Note On and Note Off)
        List<RawMidiMessage> rawMessages = new(sortedNotes.Count * 2);

        for (int i = 0; i < sortedNotes.Count; i++)
        {
            var note = sortedNotes[i];
            byte onStatus = (byte)(0x90 | (note.Channel & 0x0F));
            byte offStatus = (byte)(0x80 | (note.Channel & 0x0F));

            // Find next hit time for the same note & channel to prevent overlapping notes
            long nextHitTime = -1;
            for (int j = i + 1; j < sortedNotes.Count; j++)
            {
                if (sortedNotes[j].Note == note.Note && sortedNotes[j].Channel == note.Channel)
                {
                    nextHitTime = sortedNotes[j].TimeMs;
                    break;
                }
            }

            long duration = 50; // standard 50ms percussion duration
            if (nextHitTime > note.TimeMs)
            {
                duration = Math.Min(50, nextHitTime - note.TimeMs);
            }
            long offTime = note.TimeMs + Math.Max(1, duration);

            rawMessages.Add(new RawMidiMessage(note.TimeMs, onStatus, (byte)note.Note, (byte)note.Velocity, false));
            rawMessages.Add(new RawMidiMessage(offTime, offStatus, (byte)note.Note, 0, true));
        }

        // Sort messages: by TimeMs, then NoteOff before NoteOn if same timestamp
        rawMessages.Sort((a, b) =>
        {
            int cmp = a.TimeMs.CompareTo(b.TimeMs);
            if (cmp != 0) return cmp;
            if (a.IsNoteOff != b.IsNoteOff)
                return a.IsNoteOff ? -1 : 1;
            return a.Data1.CompareTo(b.Data1);
        });

        using MemoryStream trackStream = new(rawMessages.Count * 4 + 64);

        // Track Name Meta Event at delta 0
        WriteVariableLength(trackStream, 0);
        trackStream.WriteByte(0xFF);
        trackStream.WriteByte(0x03);
        byte[] nameBytes = "DTXMania Playback"u8.ToArray();
        trackStream.WriteByte((byte)nameBytes.Length);
        trackStream.Write(nameBytes);

        // Set Tempo Meta Event at delta 0: 500,000 microseconds per quarter note (120 BPM)
        WriteVariableLength(trackStream, 0);
        trackStream.WriteByte(0xFF);
        trackStream.WriteByte(0x51);
        trackStream.WriteByte(0x03);
        trackStream.WriteByte(0x07);
        trackStream.WriteByte(0xA1);
        trackStream.WriteByte(0x20);

        // Time Signature Meta Event at delta 0: 4/4
        WriteVariableLength(trackStream, 0);
        trackStream.WriteByte(0xFF);
        trackStream.WriteByte(0x58);
        trackStream.WriteByte(0x04);
        trackStream.WriteByte(0x04);
        trackStream.WriteByte(0x02);
        trackStream.WriteByte(0x18);
        trackStream.WriteByte(0x08);

        // Write note events
        long prevTimeMs = 0;
        for (int i = 0; i < rawMessages.Count; i++)
        {
            var msg = rawMessages[i];
            long deltaTime = Math.Max(0, msg.TimeMs - prevTimeMs);
            WriteVariableLength(trackStream, deltaTime);
            trackStream.WriteByte(msg.Status);
            trackStream.WriteByte(msg.Data1);
            trackStream.WriteByte(msg.Data2);
            prevTimeMs = msg.TimeMs;
        }

        // End of Track Meta Event
        WriteVariableLength(trackStream, 0);
        trackStream.WriteByte(0xFF);
        trackStream.WriteByte(0x2F);
        trackStream.WriteByte(0x00);

        // Write full SMF Type 0 file
        using FileStream fs = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None);

        // MThd chunk
        fs.Write("MThd"u8);
        Write32BigEndian(fs, 6);
        Write16BigEndian(fs, 0);   // Format 0: single track
        Write16BigEndian(fs, 1);   // 1 track
        Write16BigEndian(fs, 500); // 500 ticks per quarter note (1 tick = 1 ms at 120 BPM)

        // MTrk chunk
        fs.Write("MTrk"u8);
        Write32BigEndian(fs, (uint)trackStream.Length);
        trackStream.Position = 0;
        trackStream.CopyTo(fs);
    }

    private static void WriteVariableLength(Stream stream, long value)
    {
        long buffer = value & 0x7F;
        while ((value >>= 7) > 0)
        {
            buffer <<= 8;
            buffer |= 0x80;
            buffer |= (value & 0x7F);
        }
        while (true)
        {
            stream.WriteByte((byte)(buffer & 0xFF));
            if ((buffer & 0x80) != 0)
                buffer >>= 8;
            else
                break;
        }
    }

    private static void Write16BigEndian(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void Write32BigEndian(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }
}
