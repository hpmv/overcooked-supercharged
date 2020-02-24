using System;
using System.Collections.Generic;
using System.Linq;

namespace Hpmv {
    public class TimelineLayout {
        public GameActionSequences Sequences { get; set; }

        public double PixelsPerFrame { get; set; } = 2;
        public int MinFrames { get; set; } = 10;

        List<(int frame, int length)> FrameRifts = new List<(int, int)>();
        public readonly List<(double margin, double height)> Rifts = new List<(double margin, double height)>();

        public int FrameFromOffset(double offset) {
            double count = 0;
            foreach (var (margin, height) in Rifts) {
                if (margin < offset) {
                    offset -= margin + height;
                    count += margin / PixelsPerFrame;
                } else {
                    break;
                }
            }
            if (offset >= 0) {
                count += offset / PixelsPerFrame;
            }
            return (int)Math.Round(count);
        }

        public double OffsetFromFrame(int frame) {
            int offset = 0;
            foreach (var (rift, length) in FrameRifts) {
                if (rift < frame) {
                    offset += length;
                }
            }
            return (frame + offset) * PixelsPerFrame;
        }

        public void DoLayout() {
            List<List<(int time, bool start, GameActionNode node, int column)>> allNodes =
                Sequences.Actions.Select((list, column) => {
                    var result = new List<(int time, bool start, GameActionNode node, int column)>();
                    var prevTime = 0;
                    foreach (var item in list) {
                        var startTime = Math.Max(item.Predictions.StartFrame, prevTime);
                        var endTime = Math.Max(item.Predictions.EndFrame, startTime);
                        prevTime = endTime;
                        result.Add((startTime, true, item, column));
                        result.Add((endTime, false, item, column));
                    }
                    return result;
                }).ToList();
            int[] ptrs = new int[Sequences.Chefs.Count];
            List<(int time, bool start, GameActionNode node, int column)> merged = new List<(int time, bool start, GameActionNode node, int column)>();
            while (true) {
                int next = -1;
                int min = int.MaxValue;
                for (int i = 0; i < ptrs.Length; i++) {
                    if (ptrs[i] >= allNodes[i].Count) continue;
                    var item = allNodes[i][ptrs[i]];
                    if (item.time < min) {
                        min = item.time;
                        next = i;
                    }
                }
                if (next == -1) break;
                var node = allNodes[next][ptrs[next]];
                merged.Add(node);
                ptrs[next]++;
            }

            List<(int frame, int length, int endingLayoutFrame)> rifts = new List<(int, int, int)>();
            List<double> columnOffsets = Sequences.Chefs.Select(x => 0.0).ToList();
            List<int> columnLastLayoutFrames = Sequences.Chefs.Select(x => 0).ToList();
            double currentY = 0;
            int currentFrame = 0;
            int currentLayoutFrame = 0;
            foreach (var (time, start, node, column) in merged) {
                currentY += (time - currentFrame) * PixelsPerFrame;
                currentLayoutFrame += time - currentFrame;
                currentFrame = time;
                if (start) {
                    node.Predictions.LayoutTopFrame = currentFrame;
                    node.Predictions.LayoutTopOffset = currentY;
                    node.Predictions.LayoutTopMargin = currentY - columnOffsets[column];
                } else {
                    var frameLength = currentFrame - node.Predictions.LayoutTopFrame;
                    var originalFrameLength = frameLength;
                    for (int i = rifts.Count - 1; i >= 0; i--) {
                        var (frame, length, endingLayoutFrame) = rifts[i];
                        if (frame > node.Predictions.LayoutTopFrame) {
                            frameLength += length;
                        } else if (frame == node.Predictions.LayoutTopFrame) {
                            var extensibleLength = endingLayoutFrame - columnLastLayoutFrames[column];
                            frameLength += extensibleLength;
                        }
                    }
                    if (frameLength > MinFrames) {
                        frameLength = Math.Min(Math.Max(MinFrames, originalFrameLength), frameLength);
                    }
                    if (frameLength < MinFrames) {
                        var extensionLength = MinFrames - frameLength;
                        currentY += (MinFrames - frameLength) * PixelsPerFrame;
                        currentLayoutFrame += MinFrames - frameLength;
                        frameLength = MinFrames;
                        if (rifts.Count > 0 && rifts.Last().frame == currentFrame) {
                            var last = rifts.Last();
                            extensionLength += last.length;
                            rifts.RemoveAt(rifts.Count - 1);
                        }
                        rifts.Add((currentFrame, extensionLength, currentLayoutFrame));
                    }
                    node.Predictions.LayoutHeight = frameLength * PixelsPerFrame;
                    columnOffsets[column] = node.Predictions.LayoutTopOffset + node.Predictions.LayoutHeight;
                    columnLastLayoutFrames[column] = currentLayoutFrame;
                }
            }
            Rifts.Clear();
            FrameRifts.Clear();
            int prevFrame = 0;
            foreach (var (frame, length, _) in rifts) {
                Rifts.Add(((frame - prevFrame) * PixelsPerFrame, length * PixelsPerFrame));
                FrameRifts.Add((frame, length));
                prevFrame = frame;
            }
        }
    }
}