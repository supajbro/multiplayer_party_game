import { useCurrentFrame, interpolate } from "remotion";
import { Stage, Panel, Label, Head, pink, orange, cyan, clamp } from "./Design";
export const Preorders = ({
  raceAfterFrame = 240,
}: {
  raceAfterFrame?: number;
}) => {
  const f = useCurrentFrame();
  return (
    <Stage
      chapter="03 / BEFORE LAUNCH"
      note="32.5M pre-release units: Rockstar. GTA V exceeded $1B in its first three days: Take-Two, Sep 20, 2013."
    >
      {f < raceAfterFrame ? (
        <>
          <div
            style={{
              position: "absolute",
              top: 780,
              left: -100,
              width: 1300,
              height: 650,
              perspective: 1000,
              rotate: "-14deg",
            }}
          >
            {Array.from({ length: 54 }, (_, i) => (
              <div
                key={i}
                style={{
                  position: "absolute",
                  left: (i % 9) * 145,
                  top: Math.floor(i / 9) * 150,
                  translate: `0px ${-f * 0.6}px`,
                  width: 110,
                  height: 140,
                  border: `2px solid ${pink}`,
                  background: "linear-gradient(160deg,#773a75,#151324)",
                  color: orange,
                  fontFamily: "Impact",
                  fontSize: 74,
                  textAlign: "center",
                  paddingTop: 20,
                  opacity: interpolate(
                    f,
                    [i * 1.4, i * 1.4 + 14],
                    [0, 0.8],
                    clamp,
                  ),
                  boxShadow: "5px 8px 0 #050510",
                }}
              >
                VI
              </div>
            ))}
          </div>
          <Panel>
            <Label>Sold before release</Label>
            <div style={{ marginTop: 44 }}>
              <Head size={215} color={orange}>
                32.5
              </Head>
              <Head size={130}>MILLION</Head>
            </div>
            <div style={{ fontSize: 43, marginTop: 25 }}>
              COPIES • ACCORDING TO ROCKSTAR
            </div>
          </Panel>
        </>
      ) : (
        <Panel>
          <Label>The race to a billion</Label>
          <div style={{ marginTop: 60 }}>
            <Head size={110}>
              V <span style={{ color: pink }}>VS</span> VI
            </Head>
          </div>
          {[
            {
              name: "GTA V",
              cash: "$1B+",
              time: "FIRST THREE DAYS",
              color: cyan,
              v: 0.264,
            },
            {
              name: "GTA VI",
              cash: "$3.79B",
              time: "DAY ONE · EST. RETAIL",
              color: pink,
              v: 1,
            },
          ].map((b, i) => (
            <div key={b.name} style={{ marginTop: 80 }}>
              <Label color={b.color}>{b.name}</Label>
              <Head size={135}>{b.cash}</Head>
              <div
                style={{ height: 62, background: "#ffffff15", marginTop: 28 }}
              >
                <div
                  style={{
                    height: 62,
                    background: b.color,
                    width: `${interpolate(f, [raceAfterFrame + 5 + i * 12, raceAfterFrame + 65 + i * 12], [0, b.v * 100], clamp)}%`,
                    boxShadow: `0 0 50px ${b.color}66`,
                  }}
                />
              </div>
              <div
                style={{
                  fontSize: 31,
                  letterSpacing: 3,
                  marginTop: 24,
                  color: b.color,
                }}
              >
                {b.time}
              </div>
            </div>
          ))}
        </Panel>
      )}
    </Stage>
  );
};
