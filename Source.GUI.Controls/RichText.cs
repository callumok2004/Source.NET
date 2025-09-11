﻿
using CommunityToolkit.HighPerformance;

using Source.Common.Engine;
using Source.Common.GUI;
using Source.Common.Input;
using Source.Common.Utilities;

using System;
using System.Collections.Generic;

using static System.Net.Mime.MediaTypeNames;

namespace Source.GUI.Controls;

public class ClickPanel : Panel // todo
{

	public static Panel Create_RichText() => new RichText(null, null);
	public ClickPanel(RichText richText) {

	}
	internal int GetViewTextIndex() {
		throw new NotImplementedException();
	}

	internal void SetTextIndex(int i1, int i2) {

	}

	internal int GetTextIndex() {
		return 0;
	}
}

public class RichTextInterior : Panel
{
	public RichTextInterior(RichText parent, string name) : base(parent, name) {
		SetKeyboardInputEnabled(false);
		SetMouseInputEnabled(false);
		SetPaintBackgroundEnabled(false);
		SetPaintEnabled(false);
		RichText = parent;
	}

	RichText RichText;
}
public class RichText : Panel
{
	struct Fade
	{
		public double FadeStartTime;
		public double FadeLength;
		public double FadeSustain;
		public int OriginalAlpha;

		public override string ToString() {
			return $"Start {FadeStartTime}, Sustain {FadeSustain}, Length {FadeLength}, Original Alpha {OriginalAlpha}";
		}
	}
	struct FormatStreamPiece
	{
		public Color Color;
		public int PixelsIndent;
		public bool TextClickable;
		public ulong ClickableTextAction;
		public Fade Fade;
		public int TextStreamIndex;

		public override string ToString() {
			return $"Text[{TextStreamIndex}] Color {Color}, PixelsIndent {PixelsIndent}, Fade [{Fade}]";
		}
	}
	bool ResetFades;
	bool Interactive;
	bool UnusedScrollbarInvis;
	bool AllTextAlphaIsZero;
	readonly List<char> TextStream = [];
	readonly List<int> LineBreaks = [];
	readonly List<FormatStreamPiece> FormatStream = [];
	bool RecalcLineBreaks;
	int RecalculateBreaksIndex;
	bool InvalidateVerticalScrollbarSlider;
	int CursorPos;
	bool MouseSelection;
	bool MouseDragSelection;
	readonly int[] Select = new int[2];
	int PixelsIndent;
	int MaxCharCount;
	IFont? Font;
	IFont? FontUnderline;
	Color SelectionColor;
	Color SelectionTextColor;
	bool CurrentTextClickable;
	int ClickableTextIndex;
	Color DefaultTextColor;
	int DrawOffsetX;
	int DrawOffsetY;
	Panel Interior;
	string? InitialText;
	bool RecalcSavedRenderState;
	protected ScrollBar VertScrollBar;
	List<ClickPanel> ClickableTextPanels = [];
	struct RenderState
	{
		public int X, Y;
		public Color TextColor;
		public int PixelsIndent;
		public bool TextClickable;
		public int FormatStreamIndex;
	}
	RenderState CachedRenderState;
	Menu? EditMenu;

	const int MAX_BUFFER_SIZE = 999999;
	const int DRAW_OFFSET_X = 3;
	const int DRAW_OFFSET_Y = 1;
	public RichText(Panel? parent, string? name) : base(parent, name) {
		AllTextAlphaIsZero = false;
		Font = null;
		FontUnderline = null;
		RecalcLineBreaks = true;
		InitialText = null;
		CursorPos = 0;
		MouseSelection = false;
		MouseDragSelection = false;
		VertScrollBar = new ScrollBar(this, "ScrollBar", true);
		VertScrollBar.AddActionSignalTarget(this);
		RecalcSavedRenderState = true;
		MaxCharCount = 64 * 1024;
		AddActionSignalTarget(this);
		Interior = new RichTextInterior(this, null);
		Select[0] = -1;
		Select[1] = -1;
		EditMenu = null;
		SetCursor(CursorCode.IBeam);
		GotoTextEnd();
		DefaultTextColor = new(0, 0, 0, 0);
		InvalidateLineBreakStream();
		if (IsProportional()) {
			Surface.GetProportionalBase(out int width, out int height);
			Surface.GetScreenSize(out int sw, out int sh);

			DrawOffsetX = (int)(DRAW_OFFSET_X * ((float)sw / width));
			DrawOffsetY = (int)(DRAW_OFFSET_Y * ((float)sw / width));
		}
		else {
			DrawOffsetX = DRAW_OFFSET_X;
			DrawOffsetY = DRAW_OFFSET_Y;
		}

		FormatStreamPiece stream = new();
		stream.Color = DefaultTextColor;
		stream.Fade.FadeStartTime = 0.0;
		stream.Fade.FadeLength = -1.0;
		stream.PixelsIndent = 0;
		stream.TextStreamIndex = 0;
		stream.TextClickable = false;
		FormatStream.Add(stream);

		ResetFades = false;
		Interactive = true;
		UnusedScrollbarInvis = false;
	}

	public void SetDrawOffsets(int x, int y) {
		DrawOffsetX = x;
		DrawOffsetY = y;
	}
	public void SetDrawTextOnly() {
		SetDrawOffsets(0, 0);
		SetPaintBackgroundEnabled(false);
		SetPostChildPaintEnabled(false);
		Interior.SetVisible(false);
		SetVerticalScrollbar(false);
	}

	public void SetVerticalScrollbar(bool state) {
		if (VertScrollBar.IsVisible() != state) {
			VertScrollBar.SetVisible(state);
			InvalidateLineBreakStream();
			InvalidateLayout();
		}
	}

	public void SelectNoText() {
		Select[0] = 0;
		Select[1] = 1;
	}

	private void InvalidateLineBreakStream() {
		LineBreaks.Clear();
		LineBreaks.Add(MAX_BUFFER_SIZE);
		RecalculateBreaksIndex = 0;
		RecalcLineBreaks = true;
	}

	public void GotoTextEnd() {
		CursorPos = TextStream.Count;
		InvalidateVerticalScrollbarSlider = true;

		VertScrollBar.GetRange(out int min, out int max);
		VertScrollBar.SetValue(max);

		Repaint();
	}

	public void SetFont(IFont? font) {
		Font = font;
		InvalidateLayout();
		RecalcLineBreaks = true;
		Repaint();
	}

	public override void SetFgColor(in Color color) {
		Span<FormatStreamPiece> formatStream = FormatStream.AsSpan();
		if (formatStream.Length == 1 && (formatStream[0].Color == DefaultTextColor || formatStream[0].Color == GetFgColor()))
			formatStream[0].Color = color;
		base.SetFgColor(color);
	}

	public override void Paint() {
		AllTextAlphaIsZero = true;
		IFont? fontCurrent = Font;

		for (int j = 0; j < ClickableTextPanels.Count; j++)
			ClickableTextPanels[j].SetVisible(false);

		if (!HasText())
			return;

		GetSize(out int wide, out int tall);

		// var temp = TextStream.AsSpan().SliceNullTerminatedString();
		// Span<char> tempCopy = stackalloc char[temp.Length];
		// temp.CopyTo(tempCopy);
		// tempCopy.Replace('\n', 'N');
		// tempCopy.Replace('\0', 'Z');
		// Surface.DrawSetTextPos(wide, tall);
		// Surface.DrawSetTextColor(new(255, 255, 255));
		// Surface.DrawPrintText(tempCopy);

		int startIndex = GetStartDrawIndex(out int lineBreakIndexIndex);
		CurrentTextClickable = false;

		ClickableTextIndex = GetClickableTextIndexStart(startIndex);

		if (RecalcSavedRenderState)
			RecalculateDefaultState(startIndex);

		RenderState renderState = CachedRenderState;

		PixelsIndent = CachedRenderState.PixelsIndent;
		CurrentTextClickable = CachedRenderState.TextClickable;

		renderState.TextClickable = CurrentTextClickable;

		if (FormatStream.IsValidIndex(renderState.FormatStreamIndex))
			renderState.TextColor = FormatStream[renderState.FormatStreamIndex].Color;

		CalculateFade(ref renderState);

		renderState.FormatStreamIndex++;

		if (CurrentTextClickable)
			ClickableTextIndex = startIndex;

		// where to start drawing
		renderState.X = DrawOffsetX + PixelsIndent;
		renderState.Y = DrawOffsetY;

		int selection0 = -1, selection1 = -1;
		GetSelectedRange(out selection0, out selection1);

		Surface.DrawSetTextFont(fontCurrent);

		for (int i = startIndex; i < TextStream.Count && renderState.Y < tall;) {
			int nXBeforeStateChange = renderState.X;
			if (UpdateRenderState(i, ref renderState) || i == startIndex) {
				if (renderState.TextClickable != CurrentTextClickable) {
					if (renderState.TextClickable) {
						ClickableTextIndex++;
						fontCurrent = FontUnderline;
						Surface.DrawSetTextFont(fontCurrent);

						ClickPanel? clickPanel = ClickableTextPanels.IsValidIndex(ClickableTextIndex) ? ClickableTextPanels[ClickableTextIndex] : null;

						if (clickPanel != null)
							clickPanel.SetPos(renderState.X, renderState.Y);
					}
					else {
						FinishingURL(nXBeforeStateChange, renderState.Y);
						fontCurrent = Font;
						Surface.DrawSetTextFont(fontCurrent);
					}
					CurrentTextClickable = renderState.TextClickable;
				}
			}

			if (LineBreaks.IsValidIndex(lineBreakIndexIndex) && LineBreaks[lineBreakIndexIndex] <= i) {
				if (CurrentTextClickable)
					FinishingURL(renderState.X, renderState.Y);

				AddAnotherLine(ref renderState.X, ref renderState.Y);
				lineBreakIndexIndex++;

				if (renderState.TextClickable) {
					ClickableTextIndex++;
					ClickPanel? clickPanel = ClickableTextPanels.IsValidIndex(ClickableTextIndex) ? ClickableTextPanels[ClickableTextIndex] : null;
					if (clickPanel != null)
						clickPanel.SetPos(renderState.X, renderState.Y);
				}
			}

			int iLast = TextStream.Count - 1;

			if (LineBreaks.IsValidIndex(lineBreakIndexIndex) && LineBreaks[lineBreakIndexIndex] <= iLast)
				iLast = LineBreaks[lineBreakIndexIndex] - 1;

			if (FormatStream.IsValidIndex(renderState.FormatStreamIndex) && FormatStream[renderState.FormatStreamIndex].TextStreamIndex <= iLast)
				iLast = FormatStream[renderState.FormatStreamIndex].TextStreamIndex - 1;

			if (i < selection0 && iLast >= selection0)
				iLast = selection0 - 1;
			if (i >= selection0 && i < selection1 && iLast >= selection1)
				iLast = selection1 - 1;

			for (int iT = i; iT <= iLast; iT++) {
				if (char.IsControl(TextStream[iT])) {
					iLast = iT - 1;
					break;
				}
			}

			if (iLast < i) {
				if (TextStream[i] == '\t') {
					int dxTabWidth = 8 * Surface.GetCharacterWidth(fontCurrent, ' ');
					dxTabWidth = Math.Max(1, dxTabWidth);

					renderState.X = (dxTabWidth * (1 + (renderState.X / dxTabWidth)));
				}
				i++;
			}
			else {
				renderState.X += DrawString(i, iLast, ref renderState, fontCurrent);
				i = iLast + 1;
			}
		}

		if (renderState.TextClickable)
			FinishingURL(renderState.X, renderState.Y);
	}

	private int DrawString(int first, int last, ref RenderState renderState, IFont? font) {
		int fontTall = Surface.GetFontTall(font);
		int charWide = 0;
		for (int i = first; i <= last; i++)
			charWide += Surface.GetCharacterWidth(font, TextStream[i]);

		int selection0 = -1, selection1 = -1;
		GetSelectedRange(out selection0, out selection1);

		if (first >= selection0 && first < selection1) {
			Surface.DrawSetColor(SelectionColor);
			Surface.DrawFilledRect(renderState.X, renderState.Y, renderState.X + charWide, renderState.Y + 1 + fontTall);

			Surface.DrawSetTextColor(SelectionTextColor);
			AllTextAlphaIsZero = false;
		}
		else
			Surface.DrawSetTextColor(renderState.TextColor);

		if (renderState.TextColor.A != 0) {
			AllTextAlphaIsZero = false;
			Surface.DrawSetTextPos(renderState.X, renderState.Y);
			Surface.DrawPrintText(TextStream.AsSpan()[first..][..(last - first + 1)]);
		}

		return charWide;
	}

	private void FinishingURL(int x, int y) {
		if (ClickableTextPanels.IsValidIndex(ClickableTextIndex)) {
			ClickPanel clickPanel = ClickableTextPanels[ClickableTextIndex];
			clickPanel.GetPos(out int px, out int py);
			int fontTall = GetLineHeight();
			clickPanel.SetSize(Math.Max(x - px, 6), y - py + fontTall);
			clickPanel.SetVisible(true);

			if (x - px <= 0) {
				--ClickableTextIndex;
				clickPanel.SetVisible(false);
			}
		}
	}

	private bool UpdateRenderState(int textStreamPos, ref RenderState renderState) {
		if (FormatStream.IsValidIndex(renderState.FormatStreamIndex) && FormatStream[renderState.FormatStreamIndex].TextStreamIndex == textStreamPos) {
			// set the current formatting
			renderState.TextColor = FormatStream[renderState.FormatStreamIndex].Color;
			renderState.TextClickable = FormatStream[renderState.FormatStreamIndex].TextClickable;

			CalculateFade(ref renderState);

			int indentChange = FormatStream[renderState.FormatStreamIndex].PixelsIndent - renderState.PixelsIndent;
			renderState.PixelsIndent = FormatStream[renderState.FormatStreamIndex].PixelsIndent;

			if (indentChange != 0)
				renderState.X = renderState.PixelsIndent + DrawOffsetX;

			PixelsIndent = renderState.PixelsIndent;
			renderState.FormatStreamIndex++;
			return true;
		}

		return false;
	}

	private void CalculateFade(ref RenderState renderState) {
		if (FormatStream.IsValidIndex(renderState.FormatStreamIndex)) {
			if (ResetFades == false) {
				ref Fade fade = ref FormatStream.AsSpan()[renderState.FormatStreamIndex].Fade;
				if (fade.FadeLength != -1.0f) {
					float frac = (float)((fade.FadeStartTime - System.GetCurrentTime()) / fade.FadeLength);

					int alpha = (int)(frac * fade.OriginalAlpha);
					alpha = Math.Clamp(alpha, 0, fade.OriginalAlpha);

					renderState.TextColor.SetColor(renderState.TextColor.R, renderState.TextColor.G, renderState.TextColor.B, alpha);
				}
			}
		}
	}

	private void RecalculateDefaultState(int startIndex) {
		if (!HasText())
			return;

		Assert(startIndex < TextStream.Count);

		CachedRenderState.TextColor = GetFgColor();
		PixelsIndent = 0;
		CurrentTextClickable = false;
		ClickableTextIndex = GetClickableTextIndexStart(startIndex);

		GenerateRenderStateForTextStreamIndex(startIndex, ref CachedRenderState);
		RecalcSavedRenderState = false;
	}

	private void GenerateRenderStateForTextStreamIndex(int textStreamIndex, ref RenderState renderState) {
		renderState.FormatStreamIndex = FindFormatStreamIndexForTextStreamPos(textStreamIndex);

		renderState.TextColor = FormatStream[renderState.FormatStreamIndex].Color;
		renderState.PixelsIndent = FormatStream[renderState.FormatStreamIndex].PixelsIndent;
		renderState.TextClickable = FormatStream[renderState.FormatStreamIndex].TextClickable;
	}

	private int FindFormatStreamIndexForTextStreamPos(int textStreamIndex) {
		int formatStreamIndex = 0;
		for (; FormatStream.IsValidIndex(formatStreamIndex); formatStreamIndex++)
			if (FormatStream[formatStreamIndex].TextStreamIndex > textStreamIndex)
				break;

		formatStreamIndex--;
		if (!FormatStream.IsValidIndex(formatStreamIndex))
			formatStreamIndex = 0;

		return formatStreamIndex;
	}

	private int GetClickableTextIndexStart(int startIndex) {
		for (int i = 0; i < ClickableTextPanels.Count; i++)
			if (ClickableTextPanels[i].GetViewTextIndex() >= startIndex)
				return i - 1;

		return -1;
	}

	private bool HasText() => TextStream.Count != 0;

	public override void OnKillFocus(Panel? newPanel) {
		bool mouseRightClicked = Input.WasMousePressed(ButtonCode.MouseRight);
		bool mouseRightUp = Input.WasMouseReleased(ButtonCode.MouseRight);
		bool mouseRightDown = Input.IsMouseDown(ButtonCode.MouseRight);

		if (mouseRightClicked || mouseRightDown || mouseRightUp) {
			if (GetSelectedRange(out int start, out int end)) {
				CursorToPixelSpace(start, out int startX, out int startY);
				CursorToPixelSpace(end, out int endX, out int endY);
				Input.GetCursorPos(out int cursorX, out int cursorY);
				ScreenToLocal(ref cursorX, ref cursorY);

				int fontTall = GetLineHeight();
				endY = endY + fontTall;
				if ((startY < cursorY) && (endY > cursorY))
					return;
			}
		}

		SelectNone();

		base.OnKillFocus(newPanel);
	}

	private bool GetSelectedRange(out int start, out int end) {
		if (Select[0] == -1) {
			start = end = -1;
			return false;
		}

		start = Select[0];
		end = Select[1];
		if (end < start)
			(end, start) = (start, end);

		return true;
	}

	private void CursorToPixelSpace(int cursorPos, out int cx, out int cy) {
		int yStart = DrawOffsetY;
		int x = DrawOffsetX, y = yStart;
		PixelsIndent = 0;

		for (int i = GetStartDrawIndex(out int lineBreakIndexIndex); i < TextStream.Count; i++) {
			char ch = TextStream[i];

			if (cursorPos == i) {
				if (LineBreaks[lineBreakIndexIndex] == i) {
					AddAnotherLine(ref x, ref y);
					lineBreakIndexIndex++;
				}
				break;
			}

			if (LineBreaks[lineBreakIndexIndex] == i) {
				AddAnotherLine(ref x, ref y);
				lineBreakIndexIndex++;
			}

			x += Surface.GetCharacterWidth(Font, ch);
		}

		cx = x;
		cy = y;
	}

	private void AddAnotherLine(ref int x, ref int y) {
		x = DrawOffsetX + PixelsIndent;
		y += GetLineHeight() + DrawOffsetY;
	}

	private int GetStartDrawIndex(out int lineBreakIndexIndex) {
		int startIndex = 0;
		int startLine = VertScrollBar.GetValue();

		if (startLine >= LineBreaks.Count)
			startLine = LineBreaks.Count - 1;

		lineBreakIndexIndex = startLine;
		if (startLine != 0 && startLine < LineBreaks.Count)
			startIndex = LineBreaks[startLine - 1];

		return startIndex;
	}

	private int GetLineHeight() => Surface.GetFontTall(Font);
	private void SelectNone() {
		Select[0] = -1;
		Repaint();
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		Font = scheme.GetFont("Default", IsProportional());
		FontUnderline = scheme.GetFont("DefaultUnderline", IsProportional());

		SetFgColor(GetSchemeColor("RichText.TextColor", scheme));
		SetBgColor(GetSchemeColor("RichText.BgColor", scheme));

		SelectionTextColor = GetSchemeColor("RichText.SelectedTextColor", GetFgColor(), scheme);
		SelectionColor = GetSchemeColor("RichText.SelectedBgColor", scheme);

		ReadOnlySpan<char> insetX = scheme.GetResourceString("RichText.InsetX");
		if (insetX != null && insetX.Length != 0)
			SetDrawOffsets(int.TryParse(scheme.GetResourceString("RichText.InsetX"), out int i1) ? i1 : 0, int.TryParse(scheme.GetResourceString("RichText.InsetY"), out int i2) ? i2 : 0);
	}

	public void SetMaximumCharCount(int maxChars) => MaxCharCount = maxChars;


	public void GetText(Span<char> buf) => GetText(0, buf);
	public void GetText(int offset, Span<char> buf) {
		if (buf == null || buf.Length <= 0)
			return;

		int i;
		for (i = offset; i < (offset + buf.Length - 1); i++) {
			if (i >= TextStream.Count)
				break;

			buf[i - offset] = TextStream[i];
		}

		buf[i - offset] = '\0';
		buf[^1] = '\0';
	}

	public void SetText(ReadOnlySpan<char> text) {
		if (text == null)
			text = "";

		Span<char> unicode = stackalloc char[1024];

		if (text.Length > 0 && text[0] == '#') {
			ResolveLocalizedTextAndVariables(text, unicode);
			SetText(unicode);
			return;
		}

		FormatStream.Clear();
		FormatStreamPiece stream = new();
		stream.Color = GetFgColor();
		stream.Fade.FadeLength = -1.0f;
		stream.Fade.FadeStartTime = 0.0f;
		stream.PixelsIndent = 0;
		stream.TextStreamIndex = 0;
		stream.TextClickable = false;
		FormatStream.Add(stream);

		TextStream.Clear();
		if (text != null && text.Length > 0) {
			ReadOnlySpan<char> t = text.SliceNullTerminatedString();
			TextStream.EnsureCapacity(t.Length + 1);
			for (int i = 0; i < t.Length; i++)
				TextStream.Add(text[i]);
			TextStream.Add('\0');
		}

		GotoTextStart();
		SelectNone();

		InvalidateLineBreakStream();
		InvalidateLayout();
	}

	public void GotoTextStart() {
		CursorPos = 0;
		InvalidateVerticalScrollbarSlider = true;
		VertScrollBar.SetValue(0);
		Repaint();
	}

	public void InsertColorChange(in Color clr) {
		ref FormatStreamPiece prevItem = ref FormatStream.AsSpan()[FormatStream.Count - 1];
		if (prevItem.Color == clr)
			return;

		if (prevItem.TextStreamIndex == TextStream.Count) {
			prevItem.Color = clr;
		}
		else {
			FormatStreamPiece streamItem = prevItem;
			streamItem.Color = clr;
			streamItem.TextStreamIndex = TextStream.Count;
			FormatStream.Add(streamItem);
		}
	}

	public override void OnThink() {
		if (RecalcLineBreaks) {
			RecalcSavedRenderState = true;
			RecalculateLineBreaks();

			if (InvalidateVerticalScrollbarSlider)
				LayoutVerticalScrollBarSlider();
		}
	}

	public override void OnSizeChanged(int wide, int tall) {
		base.OnSizeChanged(wide, tall);
		InvalidateVerticalScrollbarSlider = true;
		InvalidateLineBreakStream();
		InvalidateLayout();

		if (VertScrollBar?.IsVisible() ?? false) {
			VertScrollBar?.MakeReadyForUse();
			Interior?.SetBounds(0, 0, wide - (VertScrollBar?.GetWide() ?? 0), tall);
		}
		else
			Interior?.SetBounds(0, 0, wide, tall);
	}
	public void ResetAllFades(bool hold, bool onlyExpired = false, double newSustain = -1) {
		ResetFades = hold;

		if (ResetFades == false) {
			Span<FormatStreamPiece> formatStream = FormatStream.AsSpan();
			for (int i = 1; i < formatStream.Length; i++) {
				ref FormatStreamPiece streamPiece = ref formatStream[i];
				if (onlyExpired == true)
					if (streamPiece.Fade.FadeStartTime >= System.GetCurrentTime())
						continue;

				if (newSustain == -1.0f)
					newSustain = streamPiece.Fade.FadeSustain;

				streamPiece.Fade.FadeStartTime = System.GetCurrentTime() + newSustain;
			}
		}
	}
	public void InsertFade(float sustain, float length) {
		Span<FormatStreamPiece> formatStream = FormatStream.AsSpan();
		ref FormatStreamPiece prevItem = ref formatStream[formatStream.Length - 1];
		if (prevItem.TextStreamIndex == formatStream.Length) {
			prevItem.Fade.FadeStartTime = System.GetCurrentTime() + sustain;
			prevItem.Fade.FadeSustain = sustain;
			prevItem.Fade.FadeLength = length;
			prevItem.Fade.OriginalAlpha = prevItem.Color.A;
		}
		else {
			FormatStreamPiece streamItem = prevItem;

			prevItem.Fade.FadeStartTime = System.GetCurrentTime() + sustain;
			prevItem.Fade.FadeLength = length;
			prevItem.Fade.FadeSustain = sustain;
			prevItem.Fade.OriginalAlpha = prevItem.Color.A;

			streamItem.TextStreamIndex = TextStream.Count;
			FormatStream.Add(streamItem);
		}
	}

	private void RecalculateLineBreaks() {
		if (!RecalcLineBreaks)
			return;

		int wide = GetWide();
		if (wide == 0)
			return;

		wide -= DrawOffsetX;

		RecalcLineBreaks = false;
		RecalcSavedRenderState = true;
		if (!HasText())
			return;

		int selection0 = -1, selection1 = -1;

		if (VertScrollBar.IsVisible())
			wide -= VertScrollBar.GetWide();

		int x = DrawOffsetX, y = DrawOffsetY;

		int wordStartIndex = 0;
		int lineStartIndex = 0;
		bool hasWord = false;
		bool justStartedNewLine = true;
		bool wordStartedOnNewLine = true;

		int startChar = 0;
		if (RecalculateBreaksIndex <= 0) {
			LineBreaks.Clear();
		}
		else {
			for (int i = RecalculateBreaksIndex + 1; i < LineBreaks.Count; ++i) {
				LineBreaks.RemoveAt(i);
				--i;
			}
			startChar = LineBreaks[RecalculateBreaksIndex];
			lineStartIndex = LineBreaks[RecalculateBreaksIndex];
			wordStartIndex = lineStartIndex;
		}

		if (TextStream[startChar] == '\r' || TextStream[startChar] == '\n') {
			startChar++;
			lineStartIndex = startChar;
		}

		int clickableTextNum = GetClickableTextIndexStart(startChar);
		clickableTextNum++;

		RenderState renderState = new();
		GenerateRenderStateForTextStreamIndex(startChar, ref renderState);
		CurrentTextClickable = false;

		IFont? font = Font;

		bool forceBreak = false;
		float lineWidthSoFar = 0;

		for (int i = startChar; i < TextStream.Count; ++i) {
			char ch = TextStream[i];
			renderState.X = x;
			if (UpdateRenderState(i, ref renderState)) {
				x = renderState.X;
				int preI = i;

				if (renderState.TextClickable != CurrentTextClickable) {
					if (renderState.TextClickable) {
						if (clickableTextNum >= ClickableTextPanels.Count)
							ClickableTextPanels.Add(new ClickPanel(this));

						ClickPanel? clickPanel = ClickableTextPanels[clickableTextNum++];
						clickPanel.SetTextIndex(preI, preI);
					}

					CurrentTextClickable = renderState.TextClickable;
				}
			}

			bool isWSpace = char.IsWhiteSpace(ch) ? true : false;

			bool previousWordStartedOnNewLine = wordStartedOnNewLine;
			int previousWordStartIndex = wordStartIndex;
			if (!isWSpace && ch != '\t' && ch != '\n' && ch != '\r') {
				if (!hasWord) {
					wordStartIndex = i;
					hasWord = true;
					wordStartedOnNewLine = justStartedNewLine;
				}
			}
			else {
				hasWord = false;
			}

			char wchBefore = '\0';
			char wchAfter = '\0';

			if (i > 0 && i > lineStartIndex && i != selection0 && i - 1 != selection1)
				wchBefore = TextStream[i - 1];
			if (i < TextStream.Count - 1 && i + 1 != selection0 && i != selection1)
				wchAfter = TextStream[i + 1];

			Surface.GetKernedCharWidth(font, ch, wchBefore, wchAfter, out float w, out float flabcA);
			lineWidthSoFar += w;

			if (Math.Floor(lineWidthSoFar + 0.6) + x > wide) {
				forceBreak = true;
			}

			if (!char.IsControl(ch)) {
				justStartedNewLine = false;
			}

			if (forceBreak || ch == '\r' || ch == '\n') {
				forceBreak = false;
				AddAnotherLine(ref x, ref y);

				if (ch == '\r' || ch == '\n') {
					lineStartIndex = i + 1;
					LineBreaks.Add(i + 1);
				}
				else if (previousWordStartedOnNewLine || previousWordStartIndex <= lineStartIndex) {
					lineStartIndex = i;
					LineBreaks.Add(i);

					if (renderState.TextClickable) {
						int oldIndex = ClickableTextPanels[clickableTextNum - 1].GetTextIndex();

						if (clickableTextNum >= ClickableTextPanels.Count)
							ClickableTextPanels.Add(new ClickPanel(this));

						ClickPanel clickPanel = ClickableTextPanels[clickableTextNum++];
						clickPanel.SetTextIndex(oldIndex, i);
					}
				}
				else {
					LineBreaks.Add(previousWordStartIndex);
					lineStartIndex = previousWordStartIndex;
					i = previousWordStartIndex;

					RenderState renderStateAtLastWord = new();
					GenerateRenderStateForTextStreamIndex(i, ref renderStateAtLastWord);

					if (renderStateAtLastWord.TextClickable && FormatStream[renderStateAtLastWord.FormatStreamIndex].TextStreamIndex < i) {
						int oldIndex = ClickableTextPanels[clickableTextNum - 1].GetTextIndex();

						if (clickableTextNum >= ClickableTextPanels.Count)
							ClickableTextPanels.Add(new ClickPanel(this));

						ClickPanel clickPanel = ClickableTextPanels[clickableTextNum++];
						clickPanel.SetTextIndex(oldIndex, i);
					}
				}

				lineWidthSoFar = 0;
				justStartedNewLine = true;
				hasWord = false;
				wordStartedOnNewLine = false;
				CurrentTextClickable = false;
				continue;
			}
		}

		LineBreaks.Add(MAX_BUFFER_SIZE);

		InvalidateVerticalScrollbarSlider = true;
	}

	private void LayoutVerticalScrollBarSlider() {
		InvalidateVerticalScrollbarSlider = false;

		int previousValue = VertScrollBar.GetValue();
		bool bCurrentlyAtEnd = false;
		VertScrollBar.GetRange(out int rmin, out int rmax);
		if (rmax != 0 && (previousValue + rmin + VertScrollBar.GetRangeWindow() == rmax))
			bCurrentlyAtEnd = true;

		GetSize(out int wide, out int tall);

		VertScrollBar.SetPos(wide - VertScrollBar.GetWide(), 0);
		VertScrollBar.SetSize(VertScrollBar.GetWide(), tall);

		int displayLines = tall / (GetLineHeight() + DrawOffsetY);
		int numLines = LineBreaks.Count;

		if (numLines <= displayLines) {
			VertScrollBar.SetEnabled(false);
			VertScrollBar.SetRange(0, numLines);
			VertScrollBar.SetRangeWindow(numLines);
			VertScrollBar.SetValue(0);

			if (UnusedScrollbarInvis)
				SetVerticalScrollbar(false);
		}
		else {
			if (UnusedScrollbarInvis)
				SetVerticalScrollbar(true);

			VertScrollBar.SetRange(0, numLines);
			VertScrollBar.SetRangeWindow(displayLines);
			VertScrollBar.SetEnabled(true);

			VertScrollBar.SetButtonPressedScrollValue(1);
			if (bCurrentlyAtEnd)
				VertScrollBar.SetValue(numLines - displayLines);

			VertScrollBar.InvalidateLayout();
			VertScrollBar.Repaint();
		}
	}

	public void InsertString(ReadOnlySpan<char> text, bool doLocalize = true) {
		if (doLocalize && text.Length > 0 && text[0] == '#') {
			Span<char> unicode = stackalloc char[1024];
			ReadOnlySpan<char> unicodeInput = ResolveLocalizedTextAndVariables(text, unicode);
			InsertString(unicodeInput, false); // If localizing, and we fail to resolve localized text, this will stack overflow
			return;
		}

		for (int i = 0; i < text.Length && text[i] != '\0'; i++)
			InsertChar(text[i]);

		InvalidateLayout();
		RecalcLineBreaks = true;
		Repaint();
	}

	private void InsertChar(char ch) {
		if (ch == '\r')
			return;

		if (MaxCharCount > 0 && TextStream.Count > MaxCharCount)
			TruncateTextStream();

		TextStream.Add(ch);

		RecalculateBreaksIndex = LineBreaks.Count - 2;
		Repaint();
	}

	private void TruncateTextStream() {
		if (MaxCharCount < 1)
			return;

		int cullPos = MaxCharCount / 2;

		TextStream.RemoveRange(0, cullPos);

		int formatIndex = FindFormatStreamIndexForTextStreamPos(cullPos);
		if (formatIndex > 0) {
			Span<FormatStreamPiece> piecesTemp = FormatStream.AsSpan();
			piecesTemp[0] = FormatStream[formatIndex];
			piecesTemp[0].TextStreamIndex = 0;
			FormatStream.RemoveRange(1, formatIndex);
		}


		Span<FormatStreamPiece> pieces = FormatStream.AsSpan();

		for (int i = 1; i < pieces.Length; i++) {
			Assert(pieces[i].TextStreamIndex > cullPos);
			pieces[i].TextStreamIndex -= cullPos;
		}

		InvalidateLineBreakStream();
		InvalidateLayout();
		InvalidateVerticalScrollbarSlider = true;
	}

	private ReadOnlySpan<char> ResolveLocalizedTextAndVariables(ReadOnlySpan<char> lookup, Span<char> outbuf) {
		if (lookup[0] == '#') {
			ulong index = Localize.FindIndex(lookup[1..]);

			if (index != UtlSymbol.UTL_INVAL_SYMBOL) {
				ReadOnlySpan<char> localized = Localize.GetValueByIndex(index);
				if (localized.Length > 0) {
					localized.CopyTo(outbuf);
					return outbuf[..localized.Length];
				}
			}
		}

		lookup.CopyTo(outbuf);

		return outbuf[..lookup.Length];
	}
}