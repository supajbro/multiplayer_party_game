import { AbsoluteFill, useCurrentFrame, interpolate } from "remotion";
import { Stage, Panel, Label, Head, pink, orange, clamp } from "./Design";
export const Ending = ({
  blackAfterFrame = 102,
}: {
  blackAfterFrame?: number;
}) => {
  const f = useCurrentFrame();
  return f < blackAfterFrame ? (
    <Stage
      chapter="05 / THE AFTERMATH"
      note="Biggest entertainment launch: according to Rockstar. Estimated worldwide retail sales."
    >
      <Panel top={430}>
        <Label>Thirteen years later</Label>
        <div style={{ marginTop: 50 }}>
          <Head size={105}>
            THE BIGGEST
            <br />
            ENTERTAINMENT
            <br />
            <span style={{ color: pink }}>LAUNCH EVER.</span>
          </Head>
        </div>
        <div style={{ fontSize: 32, color: orange, marginTop: 45 }}>
          ACCORDING TO ROCKSTAR
        </div>
        <div style={{ marginTop: 110 }}>
          <Head size={150} color={f % 24 < 12 ? pink : orange}>
            {["$3.79B", "32.5M", "$2.6M/MIN"][Math.floor(f / 34) % 3]}
          </Head>
        </div>
      </Panel>
    </Stage>
  ) : (
    <AbsoluteFill
      style={{
        background: "#030306",
        justifyContent: "center",
        padding: "0 90px",
        color: "white",
        fontFamily: "Arial",
        opacity: interpolate(
          f,
          [blackAfterFrame, blackAfterFrame + 10],
          [0, 1],
          clamp,
        ),
      }}
    >
      <Label>The next impossible number</Label>
      <div style={{ marginTop: 40, scale: 1 + (f - blackAfterFrame) * 0.0004 }}>
        <Head size={128}>
          HOW WILL
          <br />
          <span style={{ color: pink, textShadow: `0 0 60px ${pink}88` }}>
            GTA 7
          </span>
          <br />
          TOP THIS?
        </Head>
      </div>
      <div
        style={{ width: 150, height: 7, background: orange, marginTop: 65 }}
      />
    </AbsoluteFill>
  );
};
