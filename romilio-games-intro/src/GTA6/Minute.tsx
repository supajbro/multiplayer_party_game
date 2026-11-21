import { useCurrentFrame } from "remotion";
import { Stage, Panel, Label, Head, pink, orange } from "./Design";
export const Minute = () => {
  const f = useCurrentFrame();
  return (
    <Stage
      chapter="04 / DO THE MATH"
      note="Derived average: $3.79B ÷ 1,440 minutes = $2.632M/min. Includes preorders. Not continuously measured revenue."
    >
      {Array.from({ length: 20 }, (_, i) => (
        <div
          key={i}
          style={{
            position: "absolute",
            left: ((i * 181) % 1180) - 80,
            top: ((i * 277 + f * (3 + (i % 3))) % 2100) - 200,
            rotate: `${i * 29 + f / 3}deg`,
            fontSize: 80,
            color: orange,
            opacity: 0.13,
            border: `2px solid ${orange}`,
            padding: "10px 45px",
          }}
        >
          $
        </div>
      ))}
      <Panel>
        <Label>Averaged across 24 hours</Label>
        <div style={{ marginTop: 90 }}>
          <Head size={205} color={orange}>
            $2.6M
          </Head>
          <Head size={108}>
            EVERY
            <br />
            MINUTE.
          </Head>
        </div>
        <div
          style={{
            marginTop: 90,
            fontFamily: "Consolas, monospace",
            fontSize: 68,
            color: pink,
          }}
        >
          ${Math.round(((f / 30) * 3790000000) / 86400).toLocaleString("en-US")}
        </div>
        <div
          style={{
            fontSize: 29,
            letterSpacing: 3,
            marginTop: 18,
            color: "#c8bbd5",
          }}
        >
          ILLUSTRATIVE AVERAGE COUNTER
        </div>
        <div
          style={{
            fontSize: 41,
            lineHeight: 1.4,
            marginTop: 85,
            borderLeft: `5px solid ${pink}`,
            paddingLeft: 28,
          }}
        >
          Retail sales include preorders.
          <br />
          Movie receipts measure
          <br />
          something different.
        </div>
      </Panel>
    </Stage>
  );
};
