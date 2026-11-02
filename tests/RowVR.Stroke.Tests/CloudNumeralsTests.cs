using System;
using System.Collections.Generic;
using NUnit.Framework;
using RowVR.Stroke;

namespace RowVR.Stroke.Tests
{
    /// <summary>
    /// The numbers written in cloud (#102). Owner, 2026-11-02: "I don't want a board. I want
    /// something much more visually dynamic, part of the landscape/sky."
    /// </summary>
    [TestFixture]
    public class CloudNumeralsTests
    {
        const float Step = 1f / 72f;

        static CloudNumerals Settle(CloudNumerals cloud, ref double t, float seconds = 6f)
        {
            for (var i = 0; i < seconds / Step; i++)
            {
                t += Step;
                cloud.Update(t, Step);
            }
            return cloud;
        }

        static float Flat(Vec3 a, Vec3 b) => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        /// <summary>Every target has a drawn puff within this many blocks once settled.</summary>
        static float WorstMiss(CloudNumerals cloud, string text)
        {
            var worst = 0f;
            foreach (var target in CloudNumerals.Layout(text))
            {
                var best = float.MaxValue;
                foreach (var p in cloud.Puffs) best = Math.Min(best, Flat(p.Position, target.Position));
                worst = Math.Max(worst, best);
            }
            return worst;
        }

        [Test]
        public void Every_filled_cell_of_the_font_gets_a_puff()
        {
            var cells = 0;
            foreach (var c in "2:05.3")
                for (var row = 0; row < BlockFont.Rows; row++)
                    for (var column = 0; column < BlockFont.Width(c); column++)
                        if (BlockFont.Filled(c, column, row)) cells++;
            var cellPuffs = 0;
            foreach (var p in CloudNumerals.Layout("2:05.3"))
                if (p.Radius > CloudNumerals.JoinRadius) cellPuffs++;
            Assert.That(cellPuffs, Is.EqualTo(cells));
        }

        [Test]
        public void The_layout_is_centred_and_the_same_every_time()
        {
            var a = CloudNumerals.Layout("1 234");
            var b = CloudNumerals.Layout("1 234");
            Assert.That(a, Is.EqualTo(b));
            float min = float.MaxValue, max = float.MinValue;
            foreach (var p in a) { min = Math.Min(min, p.Position.X); max = Math.Max(max, p.Position.X); }
            Assert.That(Math.Abs(min + max), Is.LessThan(0.3f));
        }

        [Test]
        public void Settles_into_the_number()
        {
            var t = 0d;
            var cloud = new CloudNumerals();
            cloud.SetText("2:05.3");
            Settle(cloud, ref t);
            Assert.That(cloud.Settled, Is.EqualTo(CloudNumerals.Layout("2:05.3").Count));
            Assert.That(WorstMiss(cloud, "2:05.3"), Is.LessThan(0.1f));
        }

        [TestCase("2:05.3", "2:05.4")]
        [TestCase("9:59", "10:00")]
        [TestCase("1 234", "37")]
        [TestCase("-:--", "2:31.0")]
        public void Re_forms_into_the_new_number_and_the_spares_evaporate(string from, string to)
        {
            var t = 0d;
            var cloud = new CloudNumerals();
            cloud.SetText(from);
            Settle(cloud, ref t);
            cloud.SetText(to);
            Settle(cloud, ref t);
            Assert.That(cloud.Settled, Is.EqualTo(CloudNumerals.Layout(to).Count));
            Assert.That(cloud.Puffs.Count, Is.EqualTo(cloud.Settled), "spares gone");
            Assert.That(WorstMiss(cloud, to), Is.LessThan(0.1f));
        }

        [Test]
        public void An_unchanged_digit_keeps_its_puffs_where_they_are()
        {
            var t = 0d;
            var cloud = new CloudNumerals { Billow = 0f };
            cloud.SetText("2:05.3");
            Settle(cloud, ref t);
            var before = new List<CloudPuff>(cloud.Puffs);

            cloud.SetText("2:05.4");
            t += Step;
            cloud.Update(t, Step);

            // The leading "2" is the same text at the same place: its puffs must not wander.
            var two = CloudNumerals.Layout("2").Count;
            var moved = 0;
            foreach (var target in CloudNumerals.Layout("2:05.3"))
            {
                if (target.Position.X > -CloudNumerals.Width("2:05.3") * 0.5f + 3f) continue;
                var nearestBefore = float.MaxValue;
                foreach (var p in before) nearestBefore = Math.Min(nearestBefore, Flat(p.Position, target.Position));
                var nearestAfter = float.MaxValue;
                foreach (var p in cloud.Puffs) nearestAfter = Math.Min(nearestAfter, Flat(p.Position, target.Position));
                if (nearestAfter > nearestBefore + 0.1f) moved++;
            }
            Assert.That(two, Is.GreaterThan(0));
            Assert.That(moved, Is.EqualTo(0));
        }

        [Test]
        public void Nothing_jumps()
        {
            // Frame to frame, no drawn puff moves further than a drift could take it.
            var t = 0d;
            var cloud = new CloudNumerals();
            cloud.SetText("1:59.9");
            Settle(cloud, ref t);
            var texts = new[] { "2:00.0", "12:00", "-:--", "8" };
            foreach (var text in texts)
            {
                cloud.SetText(text);
                var previous = new List<CloudPuff>(cloud.Puffs);
                for (var i = 0; i < 72 * 3; i++)
                {
                    t += Step;
                    cloud.Update(t, Step);
                    var largest = 0f;
                    foreach (var p in cloud.Puffs)
                    {
                        var nearest = float.MaxValue;
                        foreach (var q in previous) nearest = Math.Min(nearest, Flat(p.Position, q.Position));
                        // A new puff blooms out of an existing one, at almost no size.
                        if (p.Radius > 0.1f) largest = Math.Max(largest, nearest);
                    }
                    Assert.That(largest, Is.LessThan(1.0f), $"{text} at frame {i}");
                    previous = new List<CloudPuff>(cloud.Puffs);
                }
            }
        }

        [Test]
        public void The_same_text_again_changes_nothing()
        {
            var t = 0d;
            var cloud = new CloudNumerals { Billow = 0f };
            cloud.SetText("37");
            Settle(cloud, ref t);
            var count = cloud.Puffs.Count;
            cloud.SetText("37");
            Settle(cloud, ref t, 1f);
            Assert.That(cloud.Puffs.Count, Is.EqualTo(count));
        }
    }
}
