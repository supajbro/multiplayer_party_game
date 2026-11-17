using System;
using UnityEditor;
using UnityEngine;

namespace CaptureAndPromote
{
    internal sealed class HighlightTimeline
    {
        private string currentPath;
        private int selected = -1;
        private HighlightMarker draft;
        private double addTime;
        private string addLabel = "Highlight";
        private string message;
        private Vector2 markerScroll;

        public void Draw(string path, RecordingMetadata data)
        {
            if (currentPath != path) { currentPath = path; selected = -1; draft = null; message = null; }
            if (data == null) { EditorGUILayout.HelpBox("Highlight metadata is unavailable for this recording.", MessageType.Info); return; }
            double duration = CaptureService.IsActiveMedia(path) ? CaptureService.MediaSeconds : data.durationSeconds;
            GUILayout.Label("Highlights • " + duration.ToString("F3") + "s • " + data.state, EditorStyles.boldLabel);
            Rect bounds = GUILayoutUtility.GetRect(100, 64, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bounds, new Color(.12f, .12f, .12f));
            double scale = Math.Max(duration, .001);
            for (int i = 0; i < data.highlights.Count; i++)
            {
                var marker = data.highlights[i];
                float x = bounds.x + (float)Math.Min(1, marker.timestampSeconds / scale) * (bounds.width - 4);
                Color color = PriorityColor(marker.priority);
                EditorGUI.DrawRect(new Rect(x, bounds.y + 6, i == selected ? 5 : 3, 30), color);
                EditorGUI.DrawRect(new Rect(x, bounds.y + 39, 4, 7), CategoryColor(marker.category));
                if (Event.current.type == EventType.MouseDown && bounds.Contains(Event.current.mousePosition) &&
                    Math.Abs(Event.current.mousePosition.x - x) < 5)
                {
                    Select(i, data);
                    Event.current.Use();
                }
            }
            GUI.Label(new Rect(bounds.x + 4, bounds.yMax - 18, 40, 18), "0s", EditorStyles.miniLabel);
            GUI.Label(new Rect(bounds.xMax - 82, bounds.yMax - 18, 78, 18), duration.ToString("F2") + "s", EditorStyles.miniLabel);
            GUILayout.Label("Priority  Low / Medium / High     Category colours appear below each marker.", EditorStyles.wordWrappedMiniLabel);

            // Virtualise the marker list so even dense recordings only draw visible rows.
            const float rowHeight = 22;
            markerScroll = EditorGUILayout.BeginScrollView(markerScroll, GUILayout.Height(116));
            Rect rows = GUILayoutUtility.GetRect(0, data.highlights.Count * rowHeight);
            int first = Mathf.Max(0, (int)(markerScroll.y / rowHeight));
            int end = Mathf.Min(data.highlights.Count, first + 7);
            for (int i = first; i < end; i++)
            {
                var marker = data.highlights[i];
                Rect row = new Rect(rows.x, rows.y + i * rowHeight, rows.width, rowHeight);
                Color original = GUI.color;
                GUI.color = PriorityColor(marker.priority);
                if (GUI.Button(row, marker.timestampSeconds.ToString("F3") + "s  " + marker.label + "  [" + marker.category + " / " + marker.priority + "]",
                    i == selected ? EditorStyles.miniButtonMid : EditorStyles.miniButton)) Select(i, data);
                GUI.color = original;
            }
            EditorGUILayout.EndScrollView();
            if (draft != null && selected >= 0 && selected < data.highlights.Count)
            {
                draft.label = EditorGUILayout.TextField("Label", draft.label);
                draft.timestampSeconds = EditorGUILayout.DoubleField("Time in seconds", draft.timestampSeconds);
                draft.category = EditorGUILayout.TextField("Category", draft.category);
                Enum.TryParse(draft.priority, out HighlightPriority priority);
                draft.priority = ((HighlightPriority)EditorGUILayout.EnumPopup("Priority", priority)).ToString();
                draft.description = EditorGUILayout.TextField("Description", draft.description);
                EditorGUILayout.LabelField("Source", draft.source);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Save Changes") && Validate(draft, duration))
                    {
                        draft.frame = (long)Math.Round(draft.timestampSeconds * data.frameRate);
                        data.highlights[selected] = draft;
                        Save(path, data);
                        Select(selected, data);
                    }
                    if (GUILayout.Button("Delete Highlight"))
                    {
                        data.highlights.RemoveAt(selected);
                        selected = -1; draft = null;
                        Save(path, data);
                    }
                }
            }
            GUILayout.Space(8);
            addTime = EditorGUILayout.DoubleField("New highlight time (s)", addTime);
            addLabel = EditorGUILayout.TextField("New highlight label", addLabel);
            if (GUILayout.Button("Add Highlight"))
            {
                var marker = new HighlightMarker { timestampSeconds = addTime, label = addLabel };
                if (Validate(marker, duration))
                {
                    marker.frame = (long)Math.Round(addTime * data.frameRate);
                    data.highlights.Add(marker);
                    Select(data.highlights.Count - 1, data);
                    Save(path, data);
                }
            }
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
        }

        private static Color PriorityColor(string priority) => priority == "High" ? new Color(1f, .45f, .3f) :
            priority == "Low" ? new Color(.45f, .7f, .85f) : new Color(1f, .8f, .3f);

        private static Color CategoryColor(string category)
        {
            uint hash = 2166136261;
            foreach (char c in category ?? "") hash = (hash ^ c) * 16777619;
            return Color.HSVToRGB((hash % 360) / 360f, .45f, .9f);
        }

        private void Select(int index, RecordingMetadata data)
        {
            selected = index;
            var marker = data.highlights[index];
            draft = new HighlightMarker { id = marker.id, timestampSeconds = marker.timestampSeconds, frame = marker.frame,
                label = marker.label, category = marker.category, priority = marker.priority,
                description = marker.description, source = marker.source };
        }

        private bool Validate(HighlightMarker marker, double duration)
        {
            if (double.IsNaN(marker.timestampSeconds) || double.IsInfinity(marker.timestampSeconds) ||
                marker.timestampSeconds < 0 || marker.timestampSeconds > duration || string.IsNullOrWhiteSpace(marker.label))
            {
                message = "Enter a label and a time from 0 to " + duration.ToString("F3") + " seconds.";
                return false;
            }
            return true;
        }

        private void Save(string path, RecordingMetadata data)
        {
            try { CaptureService.SaveMetadata(path, data); message = "Highlight metadata updated."; }
            catch (Exception exception) { message = "Could not save metadata. " + exception.Message; }
        }
    }
}
