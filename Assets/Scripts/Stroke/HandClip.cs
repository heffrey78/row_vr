using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace RowVR.Stroke
{
    /// <summary>
    /// A recorded sequence of <see cref="HandSample"/>, and its on-disk format.
    ///
    /// The format is deliberately a tab-separated text file rather than JSON or a binary
    /// blob. A clip's whole job is to become a committed test fixture, and this format is
    /// diffable line by line in review, parseable with no dependency outside the BCL (this
    /// assembly cannot take a package reference and stay engine-free), and cheap enough to
    /// append to on-device during a recording.
    ///
    ///   # rowclip 1
    ///   # recorded 2026-09-16T14:22:11Z
    ///   # note steady state, roughly 24 spm
    ///   # columns t lx ly lz rx ry rz hx hy hz fx fy fz
    ///   0.0000	-0.21	0.95	0.42	0.21	0.95	0.42	0.00	1.60	0.00	0.00	0.00	1.00
    /// </summary>
    public sealed class HandClip
    {
        public const string FormatHeader = "# rowclip 1";
        const int ColumnCount = 13;

        public string Note { get; set; }
        public string RecordedAtIso { get; set; }
        public IReadOnlyList<HandSample> Samples => _samples;

        readonly List<HandSample> _samples;

        public HandClip() : this(new List<HandSample>()) { }

        public HandClip(List<HandSample> samples)
        {
            _samples = samples ?? throw new ArgumentNullException(nameof(samples));
        }

        public void Add(HandSample sample) => _samples.Add(sample);

        public double DurationSeconds =>
            _samples.Count < 2 ? 0d : _samples[_samples.Count - 1].TimeSeconds - _samples[0].TimeSeconds;

        public void Write(TextWriter writer)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            var c = CultureInfo.InvariantCulture;

            writer.WriteLine(FormatHeader);
            writer.WriteLine("# recorded " + (RecordedAtIso ?? DateTime.UtcNow.ToString("o", c)));
            if (!string.IsNullOrEmpty(Note)) writer.WriteLine("# note " + Note.Replace("\n", " "));
            writer.WriteLine("# columns t lx ly lz rx ry rz hx hy hz fx fy fz");

            foreach (var s in _samples)
            {
                writer.Write(s.TimeSeconds.ToString("F4", c));
                WriteVec(writer, s.LeftHand);
                WriteVec(writer, s.RightHand);
                WriteVec(writer, s.HeadPosition);
                WriteVec(writer, s.HeadForward);
                writer.Write('\n');
            }
        }

        static void WriteVec(TextWriter w, Vec3 v)
        {
            var c = CultureInfo.InvariantCulture;
            w.Write('\t'); w.Write(v.X.ToString("F4", c));
            w.Write('\t'); w.Write(v.Y.ToString("F4", c));
            w.Write('\t'); w.Write(v.Z.ToString("F4", c));
        }

        public static HandClip Parse(TextReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));

            var clip = new HandClip();
            var c = CultureInfo.InvariantCulture;
            string line;
            var lineNumber = 0;
            var sawHeader = false;

            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                if (line.Length == 0) continue;

                // The format marker is checked against the first non-empty line, not merely
                // required to appear somewhere. Validating it late means a file that is not
                // a clip at all fails with a column-count complaint about line 1, which
                // sends the reader looking for the wrong problem.
                if (!sawHeader)
                {
                    if (!line.StartsWith(FormatHeader, StringComparison.Ordinal))
                    {
                        throw new FormatException(
                            $"Not a rowclip: expected the first line to be '{FormatHeader}', got '{Truncate(line)}'.");
                    }
                    sawHeader = true;
                    continue;
                }

                if (line[0] == '#')
                {
                    if (line.StartsWith("# note ", StringComparison.Ordinal)) clip.Note = line.Substring(7);
                    else if (line.StartsWith("# recorded ", StringComparison.Ordinal)) clip.RecordedAtIso = line.Substring(11);
                    continue;
                }

                var parts = line.Split('\t');
                if (parts.Length != ColumnCount)
                {
                    throw new FormatException(
                        $"Line {lineNumber}: expected {ColumnCount} tab-separated columns, got {parts.Length}.");
                }

                var v = new float[ColumnCount];
                for (var i = 0; i < ColumnCount; i++)
                {
                    if (!float.TryParse(parts[i], NumberStyles.Float, c, out v[i]))
                    {
                        throw new FormatException($"Line {lineNumber}, column {i + 1}: '{parts[i]}' is not a number.");
                    }
                }

                clip.Add(new HandSample(
                    v[0],
                    new Vec3(v[1], v[2], v[3]),
                    new Vec3(v[4], v[5], v[6]),
                    new Vec3(v[7], v[8], v[9]),
                    new Vec3(v[10], v[11], v[12])));
            }

            if (!sawHeader)
            {
                throw new FormatException($"Not a rowclip: the file is empty, expected '{FormatHeader}'.");
            }

            return clip;
        }

        static string Truncate(string s) => s.Length <= 40 ? s : s.Substring(0, 40) + "...";

        public static HandClip Load(string path)
        {
            using (var reader = new StreamReader(path))
            {
                return Parse(reader);
            }
        }

        public void Save(string path)
        {
            using (var writer = new StreamWriter(path))
            {
                Write(writer);
            }
        }
    }

    /// <summary>
    /// Replays a <see cref="HandClip"/> as a pose source, interpolating between recorded
    /// samples so a test can step at whatever rate it likes regardless of the rate the clip
    /// was captured at.
    /// </summary>
    public sealed class ClipHandPoseSource : IHandPoseSource
    {
        readonly HandClip _clip;
        int _cursor;

        public ClipHandPoseSource(HandClip clip)
        {
            _clip = clip ?? throw new ArgumentNullException(nameof(clip));
        }

        public double StartTime => _clip.Samples.Count > 0 ? _clip.Samples[0].TimeSeconds : 0d;
        public double EndTime => _clip.Samples.Count > 0 ? _clip.Samples[_clip.Samples.Count - 1].TimeSeconds : 0d;

        public void Rewind() => _cursor = 0;

        public bool TryGetPose(double timeSeconds, out HandSample sample)
        {
            var samples = _clip.Samples;
            if (samples.Count == 0 || timeSeconds < StartTime || timeSeconds > EndTime)
            {
                sample = default;
                return false;
            }

            // The cursor only moves forward, so sequential playback stays O(1) per step.
            // Rewind() resets it for a second pass over the same clip.
            if (_cursor > 0 && samples[_cursor].TimeSeconds > timeSeconds) _cursor = 0;
            while (_cursor < samples.Count - 2 && samples[_cursor + 1].TimeSeconds <= timeSeconds) _cursor++;

            var a = samples[_cursor];
            var b = samples[Math.Min(_cursor + 1, samples.Count - 1)];
            var span = b.TimeSeconds - a.TimeSeconds;
            var t = span > 1e-9d ? (float)((timeSeconds - a.TimeSeconds) / span) : 0f;

            sample = new HandSample(
                timeSeconds,
                Vec3.Lerp(a.LeftHand, b.LeftHand, t),
                Vec3.Lerp(a.RightHand, b.RightHand, t),
                Vec3.Lerp(a.HeadPosition, b.HeadPosition, t),
                Vec3.Lerp(a.HeadForward, b.HeadForward, t));
            return true;
        }
    }
}
