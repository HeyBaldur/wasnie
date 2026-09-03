import { findScrollContainer } from './find-scroll-container';

/**
 * These build real elements and give them real sizes, because the whole function is a question about
 * layout: a test with stubbed `scrollHeight` would pass against any implementation that reads the
 * property, including a wrong one.
 */
describe('findScrollContainer', () => {
  let root: HTMLElement;

  function el(styles: Partial<CSSStyleDeclaration>, height?: number): HTMLElement {
    const node = document.createElement('div');
    Object.assign(node.style, styles);
    if (height !== undefined) {
      const filler = document.createElement('div');
      filler.style.height = `${height}px`;
      node.appendChild(filler);
    }
    return node;
  }

  beforeEach(() => {
    root = document.createElement('div');
    document.body.appendChild(root);
  });

  afterEach(() => root.remove());

  it('returns the ancestor that both overflows and is scrollable', () => {
    const scroller = el({ height: '100px', overflowY: 'auto' });
    const inner = el({});
    const filler = el({ height: '500px' });
    inner.appendChild(filler);
    scroller.appendChild(inner);
    root.appendChild(scroller);

    expect(findScrollContainer(inner)).toBe(scroller);
  });

  /** ★ Scrollable but with nothing to scroll is not the scroller. */
  it('skips an ancestor that is scrollable but does not overflow', () => {
    const outer = el({ height: '100px', overflowY: 'auto' });
    const notScrolling = el({ height: '20px', overflowY: 'auto' });
    const inner = el({});
    notScrolling.appendChild(inner);
    outer.appendChild(notScrolling);
    outer.appendChild(el({ height: '500px' }));
    root.appendChild(outer);

    expect(findScrollContainer(inner)).toBe(outer);
  });

  /** ★ Overflowing but not scrollable is not the scroller either — its ancestor scrolls it. */
  it('skips an ancestor that overflows but is not scrollable', () => {
    const scroller = el({ height: '100px', overflowY: 'auto' });
    const tall = el({ overflowY: 'visible' });
    const inner = el({});
    tall.appendChild(inner);
    tall.appendChild(el({ height: '500px' }));
    scroller.appendChild(tall);
    root.appendChild(scroller);

    expect(findScrollContainer(inner)).toBe(scroller);
  });

  it('accepts the starting element itself when it is the scroller', () => {
    const scroller = el({ height: '100px', overflowY: 'scroll' }, 500);
    root.appendChild(scroller);

    expect(findScrollContainer(scroller)).toBe(scroller);
  });

  it('returns null when nothing up the tree scrolls', () => {
    const inner = el({});
    root.appendChild(inner);

    expect(findScrollContainer(inner)).toBeNull();
  });

  it('tolerates a null start', () => {
    expect(findScrollContainer(null)).toBeNull();
  });
});
