import { useCurrentFrame, interpolate } from "remotion";
import { Stage, Panel, Label, Head, pink, orange, cyan, clamp } from "./Design";
export const Revenue = () => {
  const f = useCurrentFrame();
  const n = Math.round(interpolate(f, [0, 90], [0, 3790000000], clamp));
  return (
    <Stage
      chapter="02 / THE NUMBER"
      note="Source: Take-Two / Rockstar, Nov 20, 2026. Company estimate. Includes preorders. Avatar: Box Office Mojo."
    >
      <Panel>
        <Label>Estimated day-one retail sales</Label>
        <div style={{ marginTop: 45 }}>
          <Head size={173} color={pink}>
            $3.79
          </Head>
          <Head size={137}>BILLION</Head>
        </div>
        <div
          style={{
            marginTop: 40,
            fontSize: 57,
            fontFamily: "Consolas, monospace",
            color: orange,
            fontWeight: 800,
          }}
        >
          ${n.toLocaleString("en-US")}
        </div>
        <div style={{ marginTop: 110 }}>
          {[
            {
              name: "GTA VI",
              v: 3.79,
              c: pink,
              detail: "DAY ONE · RETAIL SALES",
            },
            {
              name: "AVATAR",
              v: 2.9237,
              c: cyan,
              detail: "LIFETIME · WORLDWIDE BOX OFFICE",
            },
          ].map((b, i) => (
            <div key={b.name} style={{ marginBottom: 65 }}>
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  fontSize: 47,
                  fontWeight: 900,
                }}
              >
                <span>{b.name}</span>
                <span style={{ color: b.c }}>${b.v.toFixed(2)}B</span>
              </div>
              <div
                style={{ marginTop: 19, background: "#ffffff15", height: 64 }}
              >
                <div
                  style={{
                    height: "100%",
                    width: `${interpolate(f, [80 + i * 20, 155 + i * 20], [0, (b.v / 3.79) * 100], clamp)}%`,
                    background: `linear-gradient(90deg,${b.c}88,${b.c})`,
                    boxShadow: `0 0 40px ${b.c}55`,
                  }}
                />
              </div>
              <div
                style={{
                  fontSize: 25,
                  letterSpacing: 2,
                  marginTop: 18,
                  color: "#cdc3d5",
                }}
              >
                {b.detail}
              </div>
            </div>
          ))}
        </div>
        <div style={{ fontSize: 32, color: orange, lineHeight: 1.4 }}>
          Different sales measures.
          <br />A comparison of scale.
        </div>
      </Panel>
    </Stage>
  );
};
