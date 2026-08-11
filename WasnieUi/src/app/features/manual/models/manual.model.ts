/** What GET /api/manual/status answers: whether this installation has a manual to serve. */
export interface ManualStatus {
  available: boolean;
}

/** The manual as authored. Rendering is the client's job — see PdfRenderer's absence and marked's use. */
export interface ManualContent {
  markdown: string;
}

/** One heading in the rendered document, with the anchor the shortcuts jump to. */
export interface ManualHeading {
  id: string;
  /** 2 for a chapter, 3 for a sub-section. Nothing deeper is offered as a shortcut. */
  level: number;
  text: string;
}
