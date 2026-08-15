using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace FACM.League
{
    internal static class LeagueCredentialFocus
    {
        internal sealed class Candidate
        {
            public AutomationElement Element;
            public int Score;
            public double VerticalDistance;
            public string Reason;
        }

        public static bool TryFocusPasswordField(out string detail)
        {
            detail = string.Empty;
            try
            {
                var foreground = GetForegroundWindow();
                if (foreground == IntPtr.Zero)
                {
                    detail = "uia-no-foreground";
                    return false;
                }

                var root = AutomationElement.FromHandle(foreground);
                if (root == null)
                {
                    detail = "uia-no-root";
                    return false;
                }

                AutomationElement focused = null;
                try { focused = AutomationElement.FocusedElement; }
                catch { }

                var focusedRect = Rect.Empty;
                if (focused != null)
                {
                    try { focusedRect = focused.Current.BoundingRectangle; }
                    catch { }
                }

                var edits = root.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                if (edits == null || edits.Count == 0)
                {
                    detail = "uia-no-edits";
                    return false;
                }

                var candidates = new List<Candidate>();
                for (var index = 0; index < edits.Count; index++)
                {
                    var element = edits[index];
                    if (element == null) continue;

                    bool enabled;
                    bool focusable;
                    bool isPassword;
                    string name;
                    string automationId;
                    Rect rect;
                    try
                    {
                        enabled = element.Current.IsEnabled;
                        focusable = element.Current.IsKeyboardFocusable;
                        isPassword = element.Current.IsPassword;
                        name = element.Current.Name ?? string.Empty;
                        automationId = element.Current.AutomationId ?? string.Empty;
                        rect = element.Current.BoundingRectangle;
                    }
                    catch (ElementNotAvailableException)
                    {
                        continue;
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }

                    var belowFocused = !focusedRect.IsEmpty && !rect.IsEmpty && rect.Top >= focusedRect.Bottom - 4;
                    var score = ScoreCandidate(isPassword, name, automationId, enabled, focusable, belowFocused);
                    if (score <= 0) continue;

                    var distance = belowFocused && !focusedRect.IsEmpty && !rect.IsEmpty
                        ? Math.Max(0, rect.Top - focusedRect.Bottom)
                        : double.MaxValue;
                    candidates.Add(new Candidate
                    {
                        Element = element,
                        Score = score,
                        VerticalDistance = distance,
                        Reason = isPassword
                            ? "uia-password"
                            : LooksLikePassword(name, automationId) ? "uia-named" : "uia-below"
                    });
                }

                var best = candidates
                    .OrderByDescending(candidate => candidate.Score)
                    .ThenBy(candidate => candidate.VerticalDistance)
                    .FirstOrDefault();
                if (best == null || best.Element == null)
                {
                    detail = "uia-no-password-candidate";
                    return false;
                }

                best.Element.SetFocus();
                Thread.Sleep(80);
                detail = best.Reason;
                return true;
            }
            catch (ElementNotAvailableException)
            {
                detail = "uia-element-unavailable";
                return false;
            }
            catch (InvalidOperationException)
            {
                detail = "uia-invalid-operation";
                return false;
            }
            catch (COMException)
            {
                detail = "uia-com-failure";
                return false;
            }
            catch
            {
                detail = "uia-failure";
                return false;
            }
        }

        internal static int ScoreCandidate(
            bool isPassword,
            string name,
            string automationId,
            bool enabled,
            bool focusable,
            bool belowFocused)
        {
            if (!enabled || !focusable) return 0;

            var score = 1;
            if (belowFocused) score += 100;
            if (LooksLikePassword(name, automationId)) score += 5000;
            if (isPassword) score += 10000;
            return score;
        }

        private static bool LooksLikePassword(string name, string automationId)
        {
            var combined = ((name ?? string.Empty) + " " + (automationId ?? string.Empty)).ToLowerInvariant();
            return combined.Contains("password") || combined.Contains("passwd") || combined.Contains("pwd") ||
                   combined.Contains("密码");
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }
}
