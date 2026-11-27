import { cn } from "@/lib/cn";

// Code 128 bar/space module widths for symbol values 0–106 (103–105 = Start A/B/C, 106 = Stop).
const PATTERNS = [
  "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
  "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
  "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
  "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
  "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
  "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
  "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
  "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
  "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
  "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
  "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
];

const START_B = 104;
const STOP = 106;
const QUIET_ZONE = 10;

/** Encodes text as Code 128 set B. Returns null for characters outside ASCII 32–126. */
export function encodeCode128B(text: string): { modules: number[]; width: number } | null {
  if (!text) return null;
  const values: number[] = [];
  for (const ch of text) {
    const code = ch.charCodeAt(0);
    if (code < 32 || code > 126) return null;
    values.push(code - 32);
  }
  let checksum = START_B;
  values.forEach((v, i) => {
    checksum += v * (i + 1);
  });
  checksum %= 103;

  const symbols = [START_B, ...values, checksum, STOP];
  const modules = symbols.flatMap((s) => PATTERNS[s].split("").map(Number));
  const width = modules.reduce((a, b) => a + b, 0);
  return { modules, width };
}

/** Scannable Code 128 barcode rendered as crisp SVG, with the human-readable value underneath. */
export function Barcode({ value, height = 64, className, label }: { value: string; height?: number; className?: string; label?: string }) {
  const encoded = encodeCode128B(value);
  if (!encoded) {
    return <p className={cn("font-mono text-sm text-slate-700", className)}>{value}</p>;
  }

  const total = encoded.width + QUIET_ZONE * 2;
  const bars: { x: number; w: number }[] = [];
  let x = QUIET_ZONE;
  encoded.modules.forEach((w, i) => {
    if (i % 2 === 0) bars.push({ x, w }); // even index = bar, odd = space
    x += w;
  });

  return (
    <figure className={cn("inline-flex flex-col items-center rounded-md border border-slate-200 bg-white px-2 pb-1.5 pt-2", className)}>
      <svg
        role="img"
        aria-label={label ?? `Barcode ${value}`}
        viewBox={`0 0 ${total} ${height}`}
        preserveAspectRatio="none"
        shapeRendering="crispEdges"
        className="block w-full max-w-80"
        style={{ height, minWidth: Math.min(total * 1.5, 320) }}
      >
        <rect width={total} height={height} fill="#fff" />
        {bars.map((b) => (
          <rect key={b.x} x={b.x} y={0} width={b.w} height={height} fill="#0f172a" />
        ))}
      </svg>
      <figcaption className="mt-1 font-mono text-xs tracking-[0.2em] text-slate-700">{value}</figcaption>
    </figure>
  );
}
