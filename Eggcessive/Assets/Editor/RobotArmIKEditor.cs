#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RobotArmIK))]
public sealed class RobotArmIKEditor : Editor
{
    public override void OnInspectorGUI()
    {
        RobotArmIK solver = (RobotArmIK)target;
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        if (EditorGUI.EndChangeCheck())
        {
            solver.SolveNow();
            SceneView.RepaintAll();
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Choose Idle or Carry under Pose To Author, then move/rotate "
            + "the yellow Target and move the magenta Pole. The current "
            + "idle pose is used before grabbing; carry is used after.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Reset To Bind Pose"))
            {
                RecordJointUndo(solver, "Reset Robot Arm Pose");
                solver.ResetToBindPose();
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("Capture Bind Pose"))
            {
                Undo.RecordObject(solver, "Capture Robot Arm Bind Pose");
                solver.CaptureCurrentAsBindPose();
                EditorUtility.SetDirty(solver);
            }
        }

        if (GUILayout.Button(
                $"Mirror {solver.PoseToAuthor} To Opposite Arm"))
        {
            RobotArmIKMirrorUtility.MirrorToOpposite(solver, true);
        }
    }

    private void OnSceneGUI()
    {
        RobotArmIK solver = (RobotArmIK)target;
        if (!solver.PreviewInEditMode && !Application.isPlaying)
        {
            return;
        }

        DrawTargetHandle(solver);
        DrawPoleHandle(solver);
    }

    private static void DrawTargetHandle(RobotArmIK solver)
    {
        Transform armTarget = solver.GetAuthoringTarget();
        if (armTarget == null)
        {
            return;
        }

        Handles.color = Color.yellow;
        Handles.Label(
            armTarget.position + Vector3.up * HandleUtility.GetHandleSize(
                armTarget.position) * 0.12f,
            $"{solver.PoseToAuthor} Hand Target");

        EditorGUI.BeginChangeCheck();
        Quaternion positionHandleRotation = Tools.pivotRotation
            == PivotRotation.Local
            ? armTarget.rotation
            : Quaternion.identity;
        Vector3 position = Handles.PositionHandle(
            armTarget.position,
            positionHandleRotation);
        Quaternion rotation = Handles.RotationHandle(
            armTarget.rotation,
            position);
        if (!EditorGUI.EndChangeCheck())
        {
            return;
        }

        Undo.RecordObject(armTarget, "Pose Robot Arm Target");
        armTarget.SetPositionAndRotation(position, rotation);
        PrefabUtility.RecordPrefabInstancePropertyModifications(armTarget);
        solver.SolveNow();
        SceneView.RepaintAll();
    }

    private static void DrawPoleHandle(RobotArmIK solver)
    {
        Transform pole = solver.GetAuthoringPole();
        if (pole == null)
        {
            return;
        }

        Handles.color = Color.magenta;
        Handles.Label(
            pole.position + Vector3.up * HandleUtility.GetHandleSize(
                pole.position) * 0.12f,
            $"{solver.PoseToAuthor} Elbow Pole");

        EditorGUI.BeginChangeCheck();
        Vector3 position = Handles.PositionHandle(
            pole.position,
            Quaternion.identity);
        if (!EditorGUI.EndChangeCheck())
        {
            return;
        }

        Undo.RecordObject(pole, "Move Robot Arm Elbow Pole");
        pole.position = position;
        PrefabUtility.RecordPrefabInstancePropertyModifications(pole);
        solver.SolveNow();
        SceneView.RepaintAll();
    }

    private static void RecordJointUndo(RobotArmIK solver, string name)
    {
        Undo.RecordObject(solver, name);
        if (solver.UpperArm != null)
        {
            Undo.RecordObject(solver.UpperArm, name);
        }

        if (solver.Forearm != null)
        {
            Undo.RecordObject(solver.Forearm, name);
        }

        if (solver.Hand != null)
        {
            Undo.RecordObject(solver.Hand, name);
        }
    }
}

internal static class RobotArmIKMirrorUtility
{
    public static bool MirrorToOpposite(RobotArmIK source, bool logResult)
    {
        Transform sourceTarget = source != null
            ? source.GetAuthoringTarget()
            : null;
        Transform sourcePole = source != null
            ? source.GetAuthoringPole()
            : null;
        if (sourceTarget == null || sourcePole == null)
        {
            return false;
        }

        EggCollectorRobot robot = source.GetComponentInParent<EggCollectorRobot>(
            true);
        if (robot == null)
        {
            Debug.LogWarning(
                "Could not mirror the arm because it is not below an "
                + "EggCollectorRobot.",
                source);
            return false;
        }

        RobotArmIK opposite = FindOpposite(source, robot.transform);
        Transform oppositeTarget = opposite != null
            ? opposite.GetPoseTarget(source.PoseToAuthor)
            : null;
        Transform oppositePole = opposite != null
            ? opposite.GetPosePole(source.PoseToAuthor)
            : null;
        if (opposite == null
            || oppositeTarget == null
            || oppositePole == null)
        {
            Debug.LogWarning(
                "Could not find a fully configured opposing robot arm.",
                source);
            return false;
        }

        Undo.RecordObjects(
            new Object[] { oppositeTarget, oppositePole },
            "Mirror Robot Arm Pose");
        MirrorPosition(sourceTarget, oppositeTarget, robot.transform);
        MirrorRotation(sourceTarget, oppositeTarget, robot.transform);
        MirrorPosition(sourcePole, oppositePole, robot.transform);
        EditorUtility.SetDirty(oppositeTarget);
        EditorUtility.SetDirty(oppositePole);
        PrefabUtility.RecordPrefabInstancePropertyModifications(
            oppositeTarget);
        PrefabUtility.RecordPrefabInstancePropertyModifications(
            oppositePole);
        Undo.RecordObject(opposite, "Mirror Robot Arm Pose");
        opposite.PoseToAuthor = source.PoseToAuthor;
        EditorUtility.SetDirty(opposite);
        opposite.SolveNow();
        SceneView.RepaintAll();

        if (logResult)
        {
            Debug.Log(
                $"Mirrored {source.name} pose to {opposite.name}.",
                opposite);
        }

        return true;
    }

    private static RobotArmIK FindOpposite(
        RobotArmIK source,
        Transform robotRoot)
    {
        RobotArmIK[] candidates = robotRoot.GetComponentsInChildren<RobotArmIK>(
            true);
        float sourceX = robotRoot.InverseTransformPoint(
            source.UpperArm.position).x;
        RobotArmIK best = null;
        float bestScore = float.PositiveInfinity;
        for (int index = 0; index < candidates.Length; index++)
        {
            RobotArmIK candidate = candidates[index];
            if (candidate == source || candidate.UpperArm == null)
            {
                continue;
            }

            float candidateX = robotRoot.InverseTransformPoint(
                candidate.UpperArm.position).x;
            float score = Mathf.Abs(candidateX + sourceX);
            if (Mathf.Sign(candidateX) != Mathf.Sign(sourceX)
                && score < bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private static void MirrorPosition(
        Transform source,
        Transform destination,
        Transform mirrorRoot)
    {
        Vector3 localPosition = mirrorRoot.InverseTransformPoint(
            source.position);
        localPosition.x = -localPosition.x;
        destination.position = mirrorRoot.TransformPoint(localPosition);
    }

    private static void MirrorRotation(
        Transform source,
        Transform destination,
        Transform mirrorRoot)
    {
        Vector3 localForward = mirrorRoot.InverseTransformDirection(
            source.forward);
        Vector3 localUp = mirrorRoot.InverseTransformDirection(source.up);
        localForward.x = -localForward.x;
        localUp.x = -localUp.x;
        destination.rotation = Quaternion.LookRotation(
            mirrorRoot.TransformDirection(localForward),
            mirrorRoot.TransformDirection(localUp));
    }
}
#endif
