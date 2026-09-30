using System;
using NUnit.Framework;
using RowVR.Stroke;

namespace RowVR.Stroke.Tests
{
    /// <summary>The synthesised soundscape: never clips, loops without a click, and is the same every run.</summary>
    [TestFixture]
    public class SoundSynthTests
    {
        static float Peak(float[] s) { var m = 0f; foreach (var v in s) m = Math.Max(m, Math.Abs(v)); return m; }

        [Test]
        public void No_sound_clips()
        {
            foreach (var s in new[]
            {
                SoundSynth.Catch(1), SoundSynth.Finish(1), SoundSynth.Lapping(6f, 1), SoundSynth.Wind(8f, 1),
                SoundSynth.HullRush(3f, 1), SoundSynth.Bird(0, 1), SoundSynth.Bird(1, 1), SoundSynth.Bird(2, 1), SoundSynth.Bird(3, 1),
            })
            {
                Assert.That(Peak(s), Is.GreaterThan(0.3f).And.LessThanOrEqualTo(1f));
                foreach (var v in s) Assert.That(float.IsNaN(v), Is.False);
            }
        }

        [Test]
        public void Loops_run_on_into_their_start_without_a_click()
        {
            // A click is a jump between the last sample and the first bigger than the jumps
            // inside the sound itself.
            foreach (var loop in new[] { SoundSynth.Lapping(6f, 3), SoundSynth.Wind(8f, 3), SoundSynth.HullRush(3f, 3) })
            {
                var largestStep = 0f;
                for (var i = 1; i < loop.Length; i++) largestStep = Math.Max(largestStep, Math.Abs(loop[i] - loop[i - 1]));
                var seam = Math.Abs(loop[0] - loop[loop.Length - 1]);
                Assert.That(seam, Is.LessThanOrEqualTo(largestStep));
            }
        }

        [Test]
        public void Loops_are_the_length_asked_for()
        {
            Assert.That(SoundSynth.Lapping(6f, 1).Length, Is.EqualTo(6 * SoundSynth.SampleRate));
        }

        [Test]
        public void The_same_seed_gives_the_same_sound_and_different_seeds_vary()
        {
            Assert.That(SoundSynth.Catch(4), Is.EqualTo(SoundSynth.Catch(4)));
            Assert.That(SoundSynth.Catch(4), Is.Not.EqualTo(SoundSynth.Catch(5)));
        }

        [Test]
        public void Stroke_sounds_are_short_enough_not_to_overlap_the_next_stroke()
        {
            // At 36 spm a stroke is 1.7 s; the catch and finish must be long over by then.
            Assert.That(SoundSynth.Catch(1).Length, Is.LessThan(SoundSynth.SampleRate));
            Assert.That(SoundSynth.Finish(1).Length, Is.LessThan(SoundSynth.SampleRate));
        }
    }
}
