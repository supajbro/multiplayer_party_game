"""Shared editor-only geometry and route for depot_02's curved receiving conveyor."""
import math, json

def build_conveyor(e):
    box,cyl,tube,mesh,arch=(e[n] for n in ('box','cyl','tube','mesh','arch'))
    group='Conveyor';radius=2.0;half_width=1.30;height=1.25
    def block(name,p,d,mat,bevel=.04):return box(name,p,d,mat,group,bevel)
    def curve(name,cx,cy,start,end,inner,outer,z0,z1,mat,n=24):
        vertices=[]
        for z in (z0,z1):
            for r in (inner,outer):
                for i in range(n+1):
                    a=start+(end-start)*i/n;vertices.append((cx+r*math.cos(a),cy+r*math.sin(a),z))
        k=n+1;faces=[]
        for i in range(n):
            faces.extend([(i,i+1,k+i+1,k+i),(2*k+i,3*k+i,3*k+i+1,2*k+i+1),
                          (i,2*k+i,2*k+i+1,i+1),(k+i,k+i+1,3*k+i+1,3*k+i)])
        faces.extend([(0,k,3*k,2*k),(n,2*k+n,3*k+n,k+n)])
        return mesh(name,vertices,faces,mat,group)
    # Two tangential straights joined by genuine quarter-circle belt sections.
    for name,p,d in [('Feed',(15.475,1.8,.8),(3.35,2.9,.5)),('Main',(11.8,-1.75,.8),(2.9,3.1,.5)),('Nose',(9.675,-5.3,.8),(.25,2.9,.5))]:
        block('D02_Collider_Conveyor'+name,p,d,'Teal',.09)
    block('D02_BeltFeed',(15.475,1.8,1.08),(3.35,2.6,.12),'Rubber')
    block('D02_BeltMain',(11.8,-1.75,1.08),(2.6,3.1,.12),'Rubber')
    block('D02_BeltNose',(9.675,-5.3,1.08),(.25,2.6,.12),'Rubber')
    bends=[('Intake',13.8,-.2,math.pi/2,math.pi),('Exit',9.8,-3.3,0,-math.pi/2)]
    for name,cx,cy,a0,a1 in bends:
        curve('D02_Collider_Conveyor'+name+'Curve',cx,cy,a0,a1,.55,3.45,.55,1.05,'Teal')
        curve('D02_CurveBelt'+name,cx,cy,a0,a1,radius-half_width,radius+half_width,1.05,1.14,'Rubber')
        for r in (.55,3.45):
            points=[(cx+r*math.cos(a0+(a1-a0)*i/24),cy+r*math.sin(a0+(a1-a0)*i/24),1.4) for i in range(25)]
            tube('D02_CurveGuard'+name,points,.065,'Silver',group)
            tube('D02_CurveRim'+name,[(x,y,1.27) for x,y,z in points],.085,'Teal',group)
        # Roller axes follow the radius; couch heading follows the tangent.
        for i in range(13):
            a=a0+(a1-a0)*i/12
            o=cyl('D02_CurveRoller'+name,(cx+radius*math.cos(a),cy+radius*math.sin(a),1.17),.065,2.55,'Silver',group,'X',12)
            o.rotation_euler[2]=a
    for x in (14.05,14.45,14.85,15.25,15.65,16.05,16.45,16.85):cyl('D02_FeedRoller',(x,1.8,1.17),.065,2.55,'Silver',group,'Y',12)
    for i in range(9):cyl('D02_MainRoller',(11.8,-.2-i*3.1/8,1.17),.065,2.55,'Silver',group,'X',12)
    cyl('D02_DischargeRoller',(9.56,-5.3,1.17),.10,2.65,'Silver',group,'Y',16)
    # Side guards stop at the nose; there is deliberately no crossbar over the outlet.
    for x in (10.35,13.25):
        block('D02_ConveyorRim',(x,-1.75,1.27),(.16,3.1,.28),'Teal',.06)
        block('D02_ConveyorTopRail',(x,-1.75,1.46),(.14,3.1,.09),'Silver')
    for y in (.35,3.25):
        block('D02_FeedRim',(15.475,y,1.27),(3.35,.16,.28),'Teal',.06)
        block('D02_FeedTopRail',(15.475,y,1.46),(3.35,.14,.09),'Silver')
    for x,y in ((10.4,-2),(13.2,-2),(11,-4.9),(9.8,-6.6),(13,1.4),(15.8,.5),(15.8,3.1)):
        block('D02_ConveyorLeg',(x,y,.3),(.18,.22,.6),'Silver')
    for x,y,dx,dy in ((10.3,-1.5,.06,.8),(13.3,-1.5,.06,.8),(11,-5.9,.8,.06),(14.8,.3,.8,.06)):
        block('D02_ConveyorLamp',(x,y,.82),(dx,dy,.1),'Light')
    portal=arch('D02_ReceivingPortal',0,0,4.05,3.55,group=group);portal.rotation_euler[2]=math.pi/2;portal.location=(13.72,1.8,0)
    for i in range(9):block('D02_IntakeCurtain',(14.04,.1+i*.42,2.28),(.06,.4,2.25),'Rubber',.015)
    points=[]
    def point(x,y,yaw):points.append({'position':{'x':-x,'y':height,'z':-y},'yaw':yaw})
    point(14.7,1.8,90);point(13.8,1.8,90)
    for i in range(1,17):
        a=math.pi/2+i*math.pi/32;point(13.8+radius*math.cos(a),-.2+radius*math.sin(a),180-math.degrees(a))
    point(11.8,-1.7,0);point(11.8,-3.3,0)
    for i in range(1,17):
        a=-i*math.pi/32;point(9.8+radius*math.cos(a),-3.3+radius*math.sin(a),-math.degrees(a))
    point(9.55,-5.3,90)
    layout={'hiddenPosition':{'x':-16.2,'y':height,'z':-1.8},'hiddenYaw':90,
            'points':points,'dropOff':{'x':-7.9,'y':.07,'z':5.3},'exitSpeed':2.25}
    path=e['ROOT']/'ArtSource/Depot02/depot_02_conveyor.json';path.write_text(json.dumps(layout,indent=2))
    return layout
