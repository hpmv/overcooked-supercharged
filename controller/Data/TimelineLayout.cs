using System;
using System.Collections.Generic;
using System.Linq;

namespace Hpmv {
    public class TimelineLayout {
        public GameActionSequences Sequences { get; set; }
        public GameEntityRecords Records { get; set; }

        public double PixelsPerFrame { get; set; } = 2;
        public int MinLayoutFrames { get; set; } = 10;

        List<(int frame, int length)> FrameRifts = new();
        public readonly List<(double margin, double height, int frame)> Rifts = new();
        public readonly List<(double offset, double height)> CriticalSections = new();

        public int FrameFromOffset(double offset) {
            double count = 0;
            foreach (var (margin, height, _) in Rifts) {
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

        public void DoLayout(int lastEmpiricalFrame) {
            List<List<(int time, bool start, GameActionNode node, int column)>> allNodes =
                Sequences.Actions.Select((list, column) => {
                    var result = new List<(int time, bool start, GameActionNode node, int column)>();
                    foreach (var item in list) {
                        var startTime = item.Predictions.StartFrame ?? lastEmpiricalFrame;
                        var endTime = item.Predictions.EndFrame ?? lastEmpiricalFrame;
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

            // The terminologies here are sensitive. "frame" means the actual time; "layout frame"
            // means the number of frames we display on the screen. These are different because we
            // add rifts in between if we don't have enough space to display the nodes.
            List<(int frame, int startLayoutFrame, int numLayoutFrames)> rifts = new();
            List<int> columnLastLayoutFrame = Sequences.Chefs.Select(x => 0).ToList();
            foreach (var (time, start, node, column) in merged) {
                if (start) {
                    // The start layout frame is one of these cases:
                    //  - If the start frame is after the last rift, then the start layout frame is
                    //    simply the last rift's layout frame plus the number of frames in between.
                    //  - If the start frame is equal to the frame of the last rift, then the start
                    //    layout frame is either the start layout frame of the last rift, or the
                    //    column's last layout frame, whichever is greater.
                    // Note that it's not possible for the start frame to be earlier than the last
                    // rift because we iterate in an order of increasing frame.
                    var lastRift = rifts.LastOrDefault();
                    int startLayoutFrame;
                    if (time > lastRift.frame) {
                        startLayoutFrame = lastRift.startLayoutFrame + lastRift.numLayoutFrames + time - lastRift.frame;
                    } else {
                        startLayoutFrame = Math.Max(columnLastLayoutFrame[column], lastRift.startLayoutFrame);
                    }
                    node.Predictions.LayoutTopFrame = time;
                    node.Predictions.LayoutTopLayoutFrame = startLayoutFrame;
                    node.Predictions.LayoutTopMargin = (startLayoutFrame - columnLastLayoutFrame[column]) * PixelsPerFrame;
                } else {
                    var layoutFramesLength = time - node.Predictions.LayoutTopFrame;
                    var availableExtension = 0;
                    for (int i = rifts.Count - 1; i >= 0; i--) {
                        var (frame, startLayoutFrame, numLayoutFrames) = rifts[i];
                        if (frame < node.Predictions.LayoutTopFrame) {
                            break;
                        }
                        if (frame < time) {
                            // Calculate the length of the element that we're supposed to display so that the end of the element
                            // lands at the correct frame. a Max is used here, so that if the element begins in the
                            // middle of a rift, we don't count the rift layout frames before the start of the element.
                            var startCountingFrom = Math.Max(startLayoutFrame, node.Predictions.LayoutTopLayoutFrame);
                            layoutFramesLength += startLayoutFrame + numLayoutFrames - startCountingFrom;
                        } else {
                            // There is already a rift at the end frame. Allow the element to extend into the rift,
                            // but once again don't take into account any layout frames before the start of the element.
                            var startCountingFrom = Math.Max(startLayoutFrame, node.Predictions.LayoutTopLayoutFrame);
                            availableExtension += startLayoutFrame + numLayoutFrames - startCountingFrom;
                        }
                    }
                    if (layoutFramesLength + availableExtension >= MinLayoutFrames) {
                        // If it's already possible to have MinFrames without creating additional rifts,
                        // then just directly perform the layout.
                        layoutFramesLength = Math.Max(layoutFramesLength, MinLayoutFrames);
                    } else {
                        // Otherwise, we need to create a rift.
                        var riftStartLayoutFrame = node.Predictions.LayoutTopLayoutFrame + layoutFramesLength + availableExtension;
                        layoutFramesLength = MinLayoutFrames;
                        var riftEndLayoutFrame = node.Predictions.LayoutTopLayoutFrame + layoutFramesLength;
                        if (rifts.Count > 0 && rifts.Last().frame == time) {
                            // If the last rift exists at the same frame then merge into it.
                            riftStartLayoutFrame = rifts.Last().startLayoutFrame;
                            rifts.RemoveAt(rifts.Count - 1);
                        }
                        rifts.Add((time, riftStartLayoutFrame, riftEndLayoutFrame - riftStartLayoutFrame));
                    }
                    columnLastLayoutFrame[column] = node.Predictions.LayoutTopLayoutFrame + layoutFramesLength;
                    node.Predictions.LayoutHeight = layoutFramesLength * PixelsPerFrame;
                }
            }
            Rifts.Clear();
            FrameRifts.Clear();
            int prevFrame = 0;
            foreach (var (frame, _, numLayoutFrames) in rifts) {
                Rifts.Add(((frame - prevFrame) * PixelsPerFrame, numLayoutFrames * PixelsPerFrame, frame));
                FrameRifts.Add((frame, numLayoutFrames));
                prevFrame = frame;
            }

            CriticalSections.Clear();
            double? criticalSectionBeginOffset = null;
            double currentRiftTotal = 0;
            int nextRiftId = 0;
            foreach (var (time, value) in Records.CriticalSectionForWarping.changes) {
                while (nextRiftId < FrameRifts.Count && FrameRifts[nextRiftId].frame < time) {
                    var (frame, length) = FrameRifts[nextRiftId];
                    currentRiftTotal += length * PixelsPerFrame;
                    nextRiftId++;
                }
                if (criticalSectionBeginOffset == null && value > 0) {
                    criticalSectionBeginOffset = currentRiftTotal + time * PixelsPerFrame;
                } else if (criticalSectionBeginOffset != null && value == 0) {
                    CriticalSections.Add((criticalSectionBeginOffset.Value, currentRiftTotal + time * PixelsPerFrame - criticalSectionBeginOffset.Value));
                    criticalSectionBeginOffset = null;
                }
            }
        }
    }
}