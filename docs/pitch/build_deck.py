#!/usr/bin/env python
"""Generate the 'Local AI on Windows / Subtitles -> Live On-Device Voiceover' pitch deck.

Reproducible build: `python build_deck.py` writes live-voiceover.pptx beside it.
Structure follows WHY -> WHAT -> HOW. Keep in sync with live-voiceover.md.
"""
from pathlib import Path

from pptx import Presentation
from pptx.util import Inches, Pt
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN, MSO_ANCHOR
from pptx.enum.shapes import MSO_SHAPE
from pptx.oxml.ns import qn

# --- palette (Microsoft-ish) ---
INK = RGBColor(0x1F, 0x1F, 0x1F)
BLUE = RGBColor(0x00, 0x78, 0xD4)
DEEP = RGBColor(0x10, 0x3A, 0x5E)
TEAL = RGBColor(0x00, 0x99, 0x8A)
AMBER = RGBColor(0xCA, 0x5F, 0x00)
GREY = RGBColor(0x60, 0x60, 0x60)
LIGHT = RGBColor(0xF3, 0xF6, 0xFB)
WHITE = RGBColor(0xFF, 0xFF, 0xFF)
CARD = RGBColor(0xEA, 0xF1, 0xFB)
SKY = RGBColor(0xCF, 0xE0, 0xF3)
STEEL = RGBColor(0x16, 0x4A, 0x74)

FONT = "Segoe UI"
FONT_L = "Segoe UI Light"
FONT_SB = "Segoe UI Semibold"

SW, SH = Inches(13.333), Inches(7.5)


def _solid(shape, color):
    shape.fill.solid()
    shape.fill.fore_color.rgb = color
    shape.line.fill.background()


def box(slide, x, y, w, h):
    tb = slide.shapes.add_textbox(x, y, w, h)
    tf = tb.text_frame
    tf.word_wrap = True
    tf.margin_left = 0
    tf.margin_right = 0
    tf.margin_top = 0
    tf.margin_bottom = 0
    return tb


def para(tf, text, size, color=INK, bold=False, font=FONT, align=PP_ALIGN.LEFT,
         space_before=0, space_after=6, bullet=False, level=0, first=False):
    p = tf.paragraphs[0] if first and not tf.paragraphs[0].runs else tf.add_paragraph()
    p.alignment = align
    p.level = level
    p.space_before = Pt(space_before)
    p.space_after = Pt(space_after)
    r = p.add_run()
    r.text = text
    r.font.size = Pt(size)
    r.font.bold = bold
    r.font.name = font
    r.font.color.rgb = color
    _bullet(p, bullet)
    return p


def _bullet(p, on):
    pPr = p._pPr if p._pPr is not None else p.get_or_add_pPr()
    for tag in ("a:buChar", "a:buAutoNum", "a:buNone"):
        for e in pPr.findall(qn(tag)):
            pPr.remove(e)
    if on:
        pPr.append(pPr.makeelement(qn("a:buFont"), {"typeface": FONT}))
        pPr.append(pPr.makeelement(qn("a:buChar"), {"char": "\u2022"}))
    else:
        pPr.append(pPr.makeelement(qn("a:buNone"), {}))


def rect(slide, x, y, w, h, color, shape=MSO_SHAPE.RECTANGLE, line=None):
    sp = slide.shapes.add_shape(shape, x, y, w, h)
    _solid(sp, color)
    if line is not None:
        sp.line.color.rgb = line
        sp.line.width = Pt(1)
    sp.shadow.inherit = False
    return sp


def blank(prs):
    return prs.slides.add_slide(prs.slide_layouts[6])


def footer(slide, n):
    tb = box(slide, Inches(0.55), Inches(7.02), Inches(9), Inches(0.35))
    para(tb.text_frame, "WindowsNaturalVoices  \u00b7  on-device speech, offline",
         10, GREY, font=FONT, first=True, space_after=0)
    tb2 = box(slide, Inches(12.2), Inches(7.02), Inches(0.7), Inches(0.35))
    para(tb2.text_frame, str(n), 10, GREY, align=PP_ALIGN.RIGHT, first=True, space_after=0)


def accent_bar(slide, color=BLUE):
    rect(slide, 0, 0, SW, Inches(0.16), color)


def title_block(slide, kicker, title, color=BLUE):
    accent_bar(slide, color)
    tb = box(slide, Inches(0.55), Inches(0.45), Inches(12.2), Inches(0.4))
    para(tb.text_frame, kicker.upper(), 13, color, bold=True, font=FONT_SB,
         first=True, space_after=0)
    tt = box(slide, Inches(0.55), Inches(0.85), Inches(12.2), Inches(1.0))
    para(tt.text_frame, title, 30, INK, bold=True, font=FONT_SB, first=True, space_after=0)


def divider(prs, tag, title, sub=None, color=BLUE):
    s = blank(prs)
    rect(s, 0, 0, SW, SH, DEEP)
    rect(s, 0, 0, SW, Inches(0.22), color)
    rect(s, Inches(0.85), Inches(3.9), Inches(1.5), Inches(0.09), TEAL)
    tb = box(s, Inches(0.85), Inches(2.55), Inches(11.6), Inches(0.7))
    para(tb.text_frame, tag.upper(), 22, TEAL, bold=True, font=FONT_SB, first=True, space_after=0)
    tt = box(s, Inches(0.85), Inches(4.25), Inches(11.6), Inches(2.2))
    para(tt.text_frame, title, 38, WHITE, bold=True, font=FONT_SB, first=True, space_after=10)
    if sub:
        para(tt.text_frame, sub, 18, SKY, font=FONT_L, space_after=0)
    return s


def two_cards(slide, left, right, y=Inches(2.2), h=Inches(3.6)):
    """left/right = (heading, header_color, [lines...])."""
    for (head, col, lines), x in ((left, Inches(0.72)), (right, Inches(6.86))):
        rect(slide, x, y, Inches(5.75), h, WHITE, line=RGBColor(0xD9, 0xE4, 0xF0))
        rect(slide, x, y, Inches(5.75), Inches(0.62), col)
        tb = box(slide, x + Inches(0.32), y + Inches(0.12), Inches(5.1), Inches(0.5))
        para(tb.text_frame, head, 17, WHITE, bold=True, font=FONT_SB, first=True, space_after=0)
        bd = box(slide, x + Inches(0.32), y + Inches(0.85), Inches(5.1), h - Inches(1.0))
        for i, ln in enumerate(lines):
            para(bd.text_frame, ln, 14.5, INK, font=FONT, bullet=True,
                 first=(i == 0), space_after=10)


prs = Presentation()
prs.slide_width = SW
prs.slide_height = SH

# ================================================================ 1 - TITLE
s = blank(prs)
rect(s, 0, 0, SW, SH, DEEP)
rect(s, 0, 0, SW, Inches(0.22), BLUE)
rect(s, 0, Inches(4.7), SW, Inches(0.05), TEAL)
tb = box(s, Inches(0.8), Inches(1.55), Inches(11.9), Inches(2.9))
para(tb.text_frame, "Local AI on Windows:", 44, WHITE, bold=True, font=FONT_SB,
     first=True, space_after=4)
para(tb.text_frame, "the advantage that's locked away", 44, TEAL, bold=True,
     font=FONT_SB, space_after=14)
para(tb.text_frame,
     "Local AI unlocks a new class of experiences: offline, private, instant, and "
     "free. Windows already ships the on-device HD voices, the speech recognition, "
     "and the hardware to run both. The one thing missing is the public API to build "
     "on. Here's the proof, and the ask.",
     20, SKY, font=FONT_L, space_after=0)
tb2 = box(s, Inches(0.8), Inches(4.95), Inches(11.9), Inches(1.4))
para(tb2.text_frame,
     "A working reference implementation on Windows' on-device speech (HD voices + recognition),",
     16, RGBColor(0x9F, 0xC4, 0xE7), font=FONT, first=True, space_after=2)
para(tb2.text_frame, "built to show Microsoft the public API it should ship.",
     16, RGBColor(0x9F, 0xC4, 0xE7), bold=True, font=FONT_SB, space_after=0)

# ================================================================ 2 - WHY divider
divider(prs, "Why",
        "Windows is sitting on something groundbreaking",
        "Local AI models unlock scenarios cloud can't: offline, private, free, "
        "instant. The capability already ships on Windows. Access does not.")

# ================================================================ 3 - WHY: Mac proves it, Windows can go further
s = blank(prs)
rect(s, 0, 0, SW, SH, LIGHT)
title_block(s, "Why \u00b7 the opportunity", "Mac proves the model; Windows can take it further", TEAL)
two_cards(
    s,
    ("APPLE \u00b7 proven, but capped", TEAL, [
        "On-device speech recognition (SpeechAnalyzer) and neural TTS ship to apps.",
        "It proves local-first speech, in and out, is real, shipped, and wanted.",
        "But there is no cloud to graduate to: Apple cannot scale you past the device.",
    ]),
    ("WINDOWS + AZURE \u00b7 the bigger story", BLUE, [
        "The same on-device HD voices AND Live Captions recognition already ship, free.",
        "One API can scale from local to Azure with your credentials, no rewrite.",
        "First-party reach (Edge, Office, Teams, PowerPoint, Clipchamp) no rival matches.",
    ]),
)
tb = box(s, Inches(0.72), Inches(6.0), Inches(11.9), Inches(0.7))
para(tb.text_frame,
     "Mac already has almost the equivalent on-device. Only Microsoft can pair it "
     "with Azure and its own product suite, turning a proven idea into a platform "
     "advantage.",
     16, DEEP, bold=True, font=FONT_SB, first=True, space_after=0)
footer(s, 3)

# ================================================================ 4 - WHY: Windows has the tech
s = blank(prs)
rect(s, 0, 0, SW, SH, WHITE)
title_block(s, "Why \u00b7 the tech exists", "The voice is already on the device", BLUE)
tb = box(s, Inches(0.7), Inches(2.05), Inches(11.9), Inches(2.0))
for i, t in enumerate([
    ("Already installed \u00b7 shared", "One system-wide model, already offline on every machine that added a voice, so apps rely on Windows instead of bundling or downloading their own."),
    ("Near-cloud quality", "Forced-HD on-device output is near-identical to Microsoft's cloud neural voices, not the old robotic local voices."),
    ("Runs anywhere", "Synthesis is far faster than real time on an ordinary CPU, needs no NPU, and is fast enough to generate speech live on virtually any modern PC."),
]):
    rect(s, Inches(0.7), Inches(2.05 + i * 1.15), Inches(0.12), Inches(0.95), BLUE)
    tbb = box(s, Inches(1.0), Inches(2.05 + i * 1.15), Inches(11.4), Inches(0.95))
    para(tbb.text_frame, t[0], 19, INK, bold=True, font=FONT_SB, first=True, space_after=2)
    para(tbb.text_frame, t[1], 15, GREY, font=FONT, space_after=0)
rect(s, Inches(0.7), Inches(5.7), Inches(11.9), Inches(0.95), CARD)
tb = box(s, Inches(1.0), Inches(5.88), Inches(11.3), Inches(0.7))
tb.text_frame.vertical_anchor = MSO_ANCHOR.MIDDLE
para(tb.text_frame,
     "The capability ships on every Windows machine. The public API to enumerate, load, "
     "and drive it does not, so no app can build on it.",
     16, DEEP, bold=True, font=FONT_SB, first=True, space_after=0)
footer(s, 4)

# ================================================================ 5 - WHY: even Edge won't use it
s = blank(prs)
rect(s, 0, 0, SW, SH, WHITE)
title_block(s, "Why \u00b7 the cost of the gap",
            "Microsoft won't even use its own local tech", AMBER)
rect(s, Inches(0.7), Inches(2.05), Inches(5.7), Inches(2.9), RGBColor(0xFB, 0xF0, 0xE6))
tb = box(s, Inches(1.0), Inches(2.3), Inches(5.2), Inches(2.5))
para(tb.text_frame, "Edge \u201cRead Aloud\u201d", 20, AMBER, bold=True, font=FONT_SB,
     first=True, space_after=8)
para(tb.text_frame,
     "Its natural voices stream from the CLOUD (a network round-trip and server cost "
     "on every play), even though a same-class Natural HD voice now runs on the very "
     "same device, offline.",
     16, INK, font=FONT, space_after=0)
facts = [
    "Needs a connection, with no offline read-aloud at natural quality.",
    "A privacy boundary: text leaves the device to be spoken.",
    "A recurring cloud bill for something the local device could do for free.",
    "If Microsoft's own browser can't build on the local voice, no ISV can.",
]
tb = box(s, Inches(6.7), Inches(2.1), Inches(6.0), Inches(4.0))
for i, f in enumerate(facts):
    para(tb.text_frame, f, 16.5, INK, font=FONT, bullet=True, first=(i == 0), space_after=14)
tb = box(s, Inches(0.7), Inches(5.4), Inches(11.9), Inches(1.0))
para(tb.text_frame,
     "The tech is sitting idle behind a missing API. Innovation is blocked at the source.",
     18, DEEP, bold=True, font=FONT_SB, first=True, space_after=0)
footer(s, 5)

# ================================================================ 6 - WHAT divider
divider(prs, "What",
        "What local voices unlock",
        "Open the API and the same on-device voice powers new products, and fixes "
        "ones Microsoft already ships.", color=TEAL)

# ================================================================ 7 - WHAT: flagship demo
s = blank(prs)
rect(s, 0, 0, SW, SH, WHITE)
title_block(s, "What \u00b7 flagship", "Subtitles \u2192 live, multilingual voiceover", BLUE)
tb = box(s, Inches(0.7), Inches(1.95), Inches(11.9), Inches(1.9))
for i, t in enumerate([
    ("Every video already ships subtitles. Turn that track into an on-device spoken voiceover, in the viewer's language, in sync with the video.", True),
    ("Proof: \u201cZava Dental\u201d product video, voiced in English (Ava) and French (Remy), Natural HD, fully offline. Same pipeline, only the subtitle language differs.", False),
    ("A WinUI app plays the muted video and switches voiceover language LIVE, mid-play, on the device.", False),
]):
    para(tb.text_frame, t[0], 17, INK if not t[1] else DEEP, bold=t[1],
         font=FONT_SB if t[1] else FONT, bullet=(not t[1]), first=(i == 0), space_after=12)
rect(s, Inches(0.7), Inches(4.35), Inches(11.9), Inches(1.55), CARD)
tb = box(s, Inches(1.0), Inches(4.55), Inches(11.3), Inches(1.2))
para(tb.text_frame, "Honest boundary", 15, AMBER, bold=True, font=FONT_SB, first=True, space_after=4)
para(tb.text_frame,
     "No lip-sync, one voice per language, neutral delivery. This complements studio "
     "dubbing; it doesn't replace it. It's transformative for the vast middle: training, docs, "
     "demos, news, corporate comms, education, UGC, accessibility.",
     14.5, INK, font=FONT, space_after=0)
footer(s, 7)

# ================================================================ 8 - WHAT: bandwidth + a11y
s = blank(prs)
rect(s, 0, 0, SW, SH, DEEP)
rect(s, 0, 0, SW, Inches(0.16), BLUE)
tb = box(s, Inches(0.55), Inches(0.45), Inches(12), Inches(0.4))
para(tb.text_frame, "WHAT \u00b7 IT SCALES THE RIGHT WAY", 13, RGBColor(0x8F, 0xC4, 0xF0),
     bold=True, font=FONT_SB, first=True, space_after=0)
tt = box(s, Inches(0.55), Inches(0.85), Inches(12.2), Inches(0.9))
para(tt.text_frame, "One track. Every language. Almost no bytes.", 30, WHITE, bold=True,
     font=FONT_SB, first=True, space_after=0)
rect(s, Inches(0.7), Inches(2.15), Inches(5.6), Inches(2.4), STEEL)
tb = box(s, Inches(0.95), Inches(2.4), Inches(5.1), Inches(2.0))
para(tb.text_frame, "\u2248 400\u00d7", 60, TEAL, bold=True, font=FONT_SB, first=True, space_after=0)
para(tb.text_frame,
     "smaller as a subtitle (~2.6 KB) than as pre-rendered dub audio (~1.0 MB), per language.",
     16, SKY, font=FONT, space_after=0)
facts = [
    "Subtitles are ALREADY on the wire for captions \u2192 the voiceover is effectively free bandwidth.",
    "7 languages: ~18 KB of subtitles vs ~7 MB of dub audio, versus ~812 MB of duplicate dubbed videos.",
    "One asset serves BOTH captions (deaf/HoH) and spoken narration (low-vision, dyslexic, eyes-busy, learners).",
    "Instant reach into the long tail of languages no studio would fund a voice cast for.",
]
tb = box(s, Inches(6.7), Inches(2.15), Inches(6.0), Inches(4.2))
for i, f in enumerate(facts):
    para(tb.text_frame, f, 16, WHITE, font=FONT, bullet=True, first=(i == 0), space_after=14)
footer(s, 8)

# ================================================================ 9 - WHAT: fix MSFT's own products
s = blank(prs)
rect(s, 0, 0, SW, SH, LIGHT)
title_block(s, "What \u00b7 fix what already ships", "The same voice, in every app", TEAL)
tiles = [
    ("Edge Read Aloud \u2192 local", "Same HD quality with zero cloud cost, offline, private, and instant, with no round-trip.", BLUE),
    ("Teams & Office", "On-device narration, captions read aloud, and accessible documents, with no per-minute bill.", DEEP),
    ("Clipchamp voiceover \u2192 local", "Its text-to-speech voiceover runs on Azure today; on-device HD voices cut that cloud bill to zero and work offline.", TEAL),
    ("Accessibility tools", "Private, offline screen narration in the user's language, so nothing leaves the machine.", AMBER),
]
positions = [(0.72, 2.15), (6.86, 2.15), (0.72, 4.55), (6.86, 4.55)]
for (tx, ty), (h, d, col) in zip(positions, tiles):
    rect(s, Inches(tx), Inches(ty), Inches(5.75), Inches(2.1), WHITE, line=RGBColor(0xDD, 0xE6, 0xF1))
    rect(s, Inches(tx), Inches(ty), Inches(0.12), Inches(2.1), col)
    tb = box(s, Inches(tx + 0.35), Inches(ty + 0.28), Inches(5.15), Inches(1.6))
    para(tb.text_frame, h, 19, INK, bold=True, font=FONT_SB, first=True, space_after=8)
    para(tb.text_frame, d, 14.5, GREY, font=FONT, space_after=0)
footer(s, 9)

# ================================================================ 10 - WHAT: PowerPoint as a studio
s = blank(prs)
rect(s, 0, 0, SW, SH, WHITE)
title_block(s, "What \u00b7 a new creative surface", "Make every PowerPoint a video studio", TEAL)
two_cards(
    s,
    ("POWERPOINT + LOCAL VOICES", TEAL, [
        "Speaker notes become the narration script, one per slide.",
        "Render the deck to a fully narrated MP4, on the device.",
        "One deck \u2192 many languages, no re-recording.",
        "Offline, private, and free, with an Azure upsell for avatars & premium.",
    ]),
    ("CLOUD VIDEO TOOLS (e.g. Synthesia)", GREY, [
        "Per-minute / subscription cloud cost.",
        "Script and content leave the org to render.",
        "Online-only; a separate tool and export step.",
        "No on-device, private, or free tier.",
    ]),
    y=Inches(2.15), h=Inches(3.45),
)
tb = box(s, Inches(0.72), Inches(5.95), Inches(11.9), Inches(0.8))
para(tb.text_frame,
     "Turn every deck into a narrated, multilingual video, built into the tool and running on the "
     "device. Even this deck could narrate itself.",
     16, DEEP, bold=True, font=FONT_SB, first=True, space_after=0)
footer(s, 10)

# ================================================================ 11 - WHAT: performance proof
s = blank(prs)
rect(s, 0, 0, SW, SH, WHITE)
title_block(s, "What \u00b7 proof it's real", "Fast enough to switch language mid-video", BLUE)
rows = [
    ("Real-time factor (synthesis)", "\u2248 0.025\u20130.05", "~20\u201340\u00d7 faster than playback"),
    ("Steady-state synth (\u22484 s speech)", "~100 ms", "per sentence"),
    ("Time-to-first-audio (cold)", "~1.9 s", "one-time on load"),
    ("Load a 2nd voice (staging cached)", "~23 ms", "essentially free"),
    ("First synth per voice (warm-up)", "~1.1\u20131.4 s", "one-time per voice"),
]
y = 2.05
for i, (a, b, c) in enumerate(rows):
    bg = LIGHT if i % 2 == 0 else WHITE
    rect(s, Inches(0.7), Inches(y), Inches(11.9), Inches(0.7), bg)
    tb = box(s, Inches(0.95), Inches(y), Inches(6.0), Inches(0.7))
    tb.text_frame.vertical_anchor = MSO_ANCHOR.MIDDLE
    para(tb.text_frame, a, 16, INK, font=FONT, first=True, space_after=0)
    vb = box(s, Inches(7.0), Inches(y), Inches(2.6), Inches(0.7))
    vb.text_frame.vertical_anchor = MSO_ANCHOR.MIDDLE
    para(vb.text_frame, b, 18, BLUE, bold=True, font=FONT_SB, first=True, space_after=0)
    cb = box(s, Inches(9.7), Inches(y), Inches(2.9), Inches(0.7))
    cb.text_frame.vertical_anchor = MSO_ANCHOR.MIDDLE
    para(cb.text_frame, c, 13, GREY, font=FONT, first=True, space_after=0)
    y += 0.72
tb = box(s, Inches(0.7), Inches(y + 0.08), Inches(11.9), Inches(0.7))
para(tb.text_frame,
     "Pre-load the offered languages once \u2192 switching is instant: change the dropdown "
     "and the next line speaks in the new language.",
     15, DEEP, bold=True, font=FONT_SB, first=True, space_after=0)
footer(s, 11)

# ================================================================ 12 - WHAT: the listening half (STT)
s = blank(prs)
rect(s, 0, 0, SW, SH, WHITE)
title_block(s, "What \u00b7 the complete platform", "TTS is only half. Windows already listens, too.", TEAL)
tb = box(s, Inches(0.7), Inches(1.95), Inches(11.9), Inches(2.15))
for i, t in enumerate([
    ("The same story runs on the input side. Windows ships the on-device Live Captions speech recognizer, fully offline, on the CPU, with no NPU.", True),
    ("This reference implementation adds a call listener: one recognizer per speaker (advisor + customer), finals-only clean punctuated sentences, merged into one two-party transcript, the same pattern Contoso-Finance uses on Mac.", False),
    ("Pair it with the HD voices and you have a complete, round-trip speech platform, speech-in and speech-out, both on-device, both already shipping in Windows, both free, private, and offline.", False),
]):
    para(tb.text_frame, t[0], 16.5, DEEP if t[1] else INK, bold=t[1],
         font=FONT_SB if t[1] else FONT, bullet=(not t[1]), first=(i == 0), space_after=12)
rect(s, Inches(0.7), Inches(4.75), Inches(11.9), Inches(1.75), CARD)
tb = box(s, Inches(1.0), Inches(4.98), Inches(11.3), Inches(1.35))
tb.text_frame.vertical_anchor = MSO_ANCHOR.MIDDLE
para(tb.text_frame, "Speak \u2192 transcribe \u2192 understand \u2192 respond \u2192 speak",
     20, TEAL, bold=True, font=FONT_SB, first=True, space_after=6)
para(tb.text_frame,
     "Every stage runs on the device, on hardware already in Windows. Microsoft can "
     "deliver a full speech platform out of technology that already ships, no NPU and "
     "no cloud required.",
     15, DEEP, font=FONT, space_after=0)
footer(s, 12)

# ================================================================ 13 - WHAT: STT benchmark table
s = blank(prs)
rect(s, 0, 0, SW, SH, WHITE)
title_block(s, "What \u00b7 proof (listening)",
            "On-device transcription at the quality tier, on the CPU", BLUE)
tb = box(s, Inches(0.7), Inches(1.75), Inches(11.9), Inches(0.5))
para(tb.text_frame,
     "Real 58 s two-party mortgage call, normalized to a 2-leg call (one recognizer "
     "per speaker); single-stream engines doubled.",
     13.5, GREY, font=FONT, first=True, space_after=0)
# columns: engine | first sentence | peak RAM | hardware | quality
cols = [(0.7, 3.7), (4.5, 1.35), (5.95, 2.0), (8.05, 1.55), (9.65, 2.95)]
heads = ["Engine", "First final", "Peak RAM (2 legs)", "Hardware", "Quality (numbers / ITN)"]
hy = 2.2
rect(s, Inches(0.7), Inches(hy), Inches(11.9), Inches(0.5), DEEP)
for (cx, cw), h in zip(cols, heads):
    hb = box(s, Inches(cx + 0.1), Inches(hy), Inches(cw - 0.15), Inches(0.5))
    hb.text_frame.vertical_anchor = MSO_ANCHOR.MIDDLE
    para(hb.text_frame, h, 12.5, WHITE, bold=True, font=FONT_SB, first=True, space_after=0)
rows = [
    ("Live Captions (this library)", "~4 s", "~500 MB", "CPU", "ITN tier ($ / % / currency)", "ours"),
    ("Apple SpeechAnalyzer (macOS)", "~4 s", "~440 MB", "Apple ANE", "Reference: $610,000, 6.2%", "peer"),
    ("WinAI Speech Preview", "3.5 s", "~6,400 MB", "Hexagon NPU", "Best ITN (Whisper Turbo)", "peer"),
    ("Nemotron 0.6B (Foundry Local)", "1.2 s", "~1,750 MB", "CPU", "No clean sentence breaks", "out"),
    ("Whisper small (CPU ONNX)", "2.3 s", "~1,200 MB", "CPU", "Low: hallucinates numbers", "out"),
]
ry = hy + 0.5
for cells in rows:
    tag = cells[5]
    bg = RGBColor(0xE4, 0xF3, 0xF0) if tag == "ours" else (LIGHT if tag == "peer" else WHITE)
    rect(s, Inches(0.7), Inches(ry), Inches(11.9), Inches(0.56), bg)
    if tag == "ours":
        rect(s, Inches(0.7), Inches(ry), Inches(0.1), Inches(0.56), TEAL)
    for (cx, cw), val, idx in zip(cols, cells[:5], range(5)):
        cb = box(s, Inches(cx + 0.1), Inches(ry), Inches(cw - 0.15), Inches(0.56))
        cb.text_frame.vertical_anchor = MSO_ANCHOR.MIDDLE
        col = INK if idx == 0 else GREY
        bold = (idx == 0)
        if idx == 2 and tag in ("ours", "peer"):
            col, bold = BLUE, True
        para(cb.text_frame, val, 12.5, col, bold=bold,
             font=FONT_SB if bold else FONT, first=True, space_after=0)
    ry += 0.56
rect(s, Inches(0.7), Inches(ry + 0.12), Inches(11.9), Inches(1.05), CARD)
tb = box(s, Inches(1.0), Inches(ry + 0.28), Inches(11.3), Inches(0.75))
tb.text_frame.vertical_anchor = MSO_ANCHOR.MIDDLE
para(tb.text_frame,
     "The quality peers are Apple and NPU Whisper Turbo. This library matches that "
     "ITN tier on the CPU at ~500 MB for two legs: ~13\u00d7 less RAM than the NPU path, "
     "no NPU. Nemotron and Whisper small are ruled out on quality, not memory.",
     14.5, DEEP, bold=True, font=FONT_SB, first=True, space_after=0)
footer(s, 13)
divider(prs, "How",
        "Make the POC real: remove the hack, ship the API",
        "One API surface. Local-first and free by default, and the same code scales "
        "to Azure with your creds.", color=AMBER)

# ================================================================ 13 - HOW: today it's a hack
s = blank(prs)
rect(s, 0, 0, SW, SH, WHITE)
title_block(s, "How \u00b7 the friction today", "It works, but only as a hack", AMBER)
tb = box(s, Inches(0.7), Inches(2.0), Inches(11.9), Inches(1.4))
para(tb.text_frame,
     "To reach the on-device HD voice and Live Captions runtimes with no public API, "
     "this reference implementation has to:",
     17, INK, font=FONT, first=True, space_after=10)
hacks = [
    "Pass an undocumented Embedded Speech license string to unlock synthesis.",
    "Load gated extension DLLs out of a first-party SystemApps folder.",
    "Resolve native dependencies by walking the package graph by hand.",
]
tb = box(s, Inches(1.0), Inches(3.15), Inches(11.4), Inches(1.9))
for i, t in enumerate(hacks):
    para(tb.text_frame, t, 17, INK, font=FONT, bullet=True, first=(i == 0), space_after=10)
rect(s, Inches(0.7), Inches(5.35), Inches(11.9), Inches(1.05), RGBColor(0xFB, 0xF0, 0xE6))
tb = box(s, Inches(1.0), Inches(5.55), Inches(11.3), Inches(0.75))
tb.text_frame.vertical_anchor = MSO_ANCHOR.MIDDLE
para(tb.text_frame,
     "Unsupported and fragile, exactly the friction that stops real products from "
     "shipping on the capability. That's what the API removes.",
     16, AMBER, bold=True, font=FONT_SB, first=True, space_after=0)
footer(s, 15)

# ================================================================ 14 - HOW: one API, local + cloud upsell
s = blank(prs)
rect(s, 0, 0, SW, SH, LIGHT)
title_block(s, "How \u00b7 the design", "One API. Local by default. Cloud when you want it.", BLUE)
# Local card
rect(s, Inches(0.72), Inches(2.05), Inches(5.75), Inches(3.0), WHITE, line=RGBColor(0xD9, 0xE4, 0xF0))
rect(s, Inches(0.72), Inches(2.05), Inches(5.75), Inches(0.62), TEAL)
tb = box(s, Inches(1.04), Inches(2.17), Inches(5.1), Inches(0.5))
para(tb.text_frame, "DEFAULT: on-device", 16, WHITE, bold=True, font=FONT_SB, first=True, space_after=0)
tb = box(s, Inches(1.04), Inches(2.9), Inches(5.1), Inches(2.0))
for i, t in enumerate([
    "Offline, private, free, instant.",
    "Runs on the installed Natural HD voice.",
    "No key, no bill, nothing leaves the device.",
]):
    para(tb.text_frame, t, 15, INK, font=FONT, bullet=True, first=(i == 0), space_after=9)
# Cloud card
rect(s, Inches(6.86), Inches(2.05), Inches(5.75), Inches(3.0), WHITE, line=RGBColor(0xD9, 0xE4, 0xF0))
rect(s, Inches(6.86), Inches(2.05), Inches(5.75), Inches(0.62), BLUE)
tb = box(s, Inches(7.18), Inches(2.17), Inches(5.1), Inches(0.5))
para(tb.text_frame, "UPSELL: same API + your Azure creds", 16, WHITE, bold=True, font=FONT_SB, first=True, space_after=0)
tb = box(s, Inches(7.18), Inches(2.9), Inches(5.1), Inches(2.0))
for i, t in enumerate([
    "More voices, more languages, server-side scale.",
    "Batch, streaming, and cross-device workloads.",
    "Same code path; credentials switch the backend.",
]):
    para(tb.text_frame, t, 15, INK, font=FONT, bullet=True, first=(i == 0), space_after=9)
# arrow between
rect(s, Inches(6.4), Inches(3.35), Inches(0.55), Inches(0.4), AMBER, MSO_SHAPE.RIGHT_ARROW)
rect(s, Inches(0.72), Inches(5.35), Inches(11.9), Inches(1.05), CARD)
tb = box(s, Inches(1.02), Inches(5.55), Inches(11.3), Inches(0.75))
tb.text_frame.vertical_anchor = MSO_ANCHOR.MIDDLE
para(tb.text_frame,
     "Local is the free on-ramp; Azure is the paid upsell. Apple stops at the "
     "device, with no cloud to graduate to. Windows plus Azure plus Microsoft's "
     "own apps is a funnel no competitor can match.",
     16, DEEP, bold=True, font=FONT_SB, first=True, space_after=0)
footer(s, 16)

# ================================================================ 15 - HOW: the ask / close
s = blank(prs)
rect(s, 0, 0, SW, SH, DEEP)
rect(s, 0, 0, SW, Inches(0.16), BLUE)
tb = box(s, Inches(0.7), Inches(0.65), Inches(12), Inches(1.2))
para(tb.text_frame, "THE ASK TO MICROSOFT", 14, RGBColor(0x8F, 0xC4, 0xF0), bold=True,
     font=FONT_SB, first=True, space_after=6)
para(tb.text_frame, "Ship this as a first-class Windows speech API", 30, WHITE, bold=True,
     font=FONT_SB, space_after=0)
tb = box(s, Inches(0.7), Inches(2.0), Inches(11.9), Inches(1.1))
para(tb.text_frame,
     "This repo is a working reference implementation of what it should look like. "
     "Take the hack out; make it supported:",
     16, SKY, font=FONT, first=True, space_after=0)
bl = [
    "Enumerate installed Natural voices and synthesize offline through the on-device HD runtime.",
    "Stream synthesis live to the speaker with word-boundary events.",
    "Recognize speech on-device with the same Live Captions model, one recognizer per audio source.",
    "Same surface scales to Azure with credentials, so it's local-first and cloud-optional.",
]
tb = box(s, Inches(1.0), Inches(3.15), Inches(11.4), Inches(2.2))
for i, t in enumerate(bl):
    para(tb.text_frame, t, 18, WHITE, font=FONT, bullet=True, first=(i == 0), space_after=10)
rect(s, Inches(0.7), Inches(5.65), Inches(11.9), Inches(1.05), STEEL)
tb = box(s, Inches(1.0), Inches(5.82), Inches(11.3), Inches(0.75))
tb.text_frame.vertical_anchor = MSO_ANCHOR.MIDDLE
para(tb.text_frame,
     "Then every app (Edge, Teams, Office, media players, accessibility tools) gets "
     "instant, private, multilingual, on-device narration and transcription for free, "
     "with a paved road to Azure.",
     16, WHITE, bold=True, font=FONT_SB, first=True, space_after=0)

out = Path(__file__).with_name("live-voiceover.pptx")
prs.save(out)
print(f"wrote {out}  ({out.stat().st_size:,} bytes, {len(prs.slides._sldIdLst)} slides)")
