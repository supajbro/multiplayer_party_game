using System;
using System.Collections.Generic;

namespace CouchGuys.Gameplay.Delivery
{
    /// <summary>A shuffled bag: every distinct nonblank line plays once before reshuffling.</summary>
    internal sealed class DispatcherQuipDeck
    {
        private readonly string[] m_lines;
        private readonly Random m_random;
        private int m_next;
        private string m_last;

        public DispatcherQuipDeck(string[] lines, Random random)
        {
            m_random = random;
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cleaned = new List<string>();
            if (lines != null)
                foreach (string line in lines)
                    if (!string.IsNullOrWhiteSpace(line) && unique.Add(line.Trim())) cleaned.Add(line.Trim());
            m_lines = cleaned.ToArray();
            m_next = m_lines.Length;
        }

        public bool TryNext(out string line)
        {
            line = null;
            if (m_lines.Length == 0) return false;
            if (m_next == m_lines.Length)
            {
                for (int i = m_lines.Length - 1; i > 0; i--)
                {
                    int j = m_random.Next(i + 1);
                    string swap = m_lines[i]; m_lines[i] = m_lines[j]; m_lines[j] = swap;
                }
                // Avoid a repeat across bag boundaries without biasing every shuffle.
                if (m_lines.Length > 1 && m_lines[0] == m_last)
                {
                    int j = m_random.Next(1, m_lines.Length);
                    string swap = m_lines[0]; m_lines[0] = m_lines[j]; m_lines[j] = swap;
                }
                m_next = 0;
            }
            line = m_last = m_lines[m_next++];
            return true;
        }
    }
}
