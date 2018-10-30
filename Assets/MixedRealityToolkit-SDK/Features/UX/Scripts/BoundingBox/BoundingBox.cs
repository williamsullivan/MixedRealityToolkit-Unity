﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.MixedReality.Toolkit.Core.EventDatum.Input;
using Microsoft.MixedReality.Toolkit.Core.Interfaces.InputSystem;
using Microsoft.MixedReality.Toolkit.Core.Interfaces.InputSystem.Handlers;
using Microsoft.MixedReality.Toolkit.Core.Definitions.Utilities;
using Microsoft.MixedReality.Toolkit.Core.Managers;
using Microsoft.MixedReality.Toolkit.InputSystem.Sources;
using System.Collections.Generic;
using UnityEngine;

namespace Microsoft.MixedReality.Toolkit.SDK.UX
{
    public class BoundingBox : MonoBehaviour, IMixedRealityPointerHandler, IMixedRealityInputHandler, IMixedRealityGestureHandler, IMixedRealitySpatialInputHandler, IMixedRealitySourceStateHandler
    {
        #region Enums
        private enum FlattenModeType
        {
            DoNotFlatten = 0,
            /// <summary>
            /// Flatten the X axis
            /// </summary>
            FlattenX,
            /// <summary>
            /// Flatten the Y axis
            /// </summary>
            FlattenY,
            /// <summary>
            /// Flatten the Z axis
            /// </summary>
            FlattenZ,
            /// <summary>
            /// Flatten the smallest relative axis if it falls below threshold
            /// </summary>
            FlattenAuto,
        }
        private enum HandleType
        {
            none = 0,
            rotation,
            scale
        }
        private enum WireframeType
        {
            Cubic = 0,
            Cylindrical
        }
        private enum CardinalAxisType
        {
            X = 0,
            Y,
            Z
        }
        private enum BoundsCalculationMethod
        {
            Collider = 0,
            Colliders,
            Renderers,
            MeshFilters
        }

        private enum HandleMoveType
        {
            Ray = 0,
            Point
        }
        #endregion Enums

        #region Serialized Fields
        [Header("Bounds Calculation")]
        [Tooltip("For complex objects, automatic bounds calculation may not behave as expected. Use an existing Box Collider (even on a child object) to manually determine bounds of Bounding Box.")]
        [SerializeField]
        private BoxCollider BoxColliderToUse = null;

        [Header("Behavior")]
        [SerializeField]
        private bool activateOnStart = false;
        [SerializeField]
        private float scaleMaximum = 2.0f;
        [SerializeField]
        private float scaleMinimum = 0.2f;

        [Header("Wireframe")]
        [SerializeField]
        private bool wireframeOnly = false;
        public bool WireframeOnly
        {
            get { return wireframeOnly; }
            set
            {
                if (wireframeOnly != value)
                {
                    wireframeOnly = value;
                    ResetHandleVisibility();
                }
            }
        }
        [SerializeField]
        private Vector3 wireframePadding = Vector3.zero;
        [SerializeField]
        private FlattenModeType flattenAxis = FlattenModeType.DoNotFlatten;
        [SerializeField]
        private WireframeType wireframeShape = WireframeType.Cubic;
        [SerializeField]
        private Material wireframeMaterial;

        [Header("Handles")]
        [Tooltip("Default materials will be created for Handles and Wireframe if none is specified.")]
        [SerializeField]
        private Material handleMaterial;
        [SerializeField]
        private Material handleGrabbedMaterial;
        [SerializeField]
        private bool showScaleHandles = true;
        public bool ShowScaleHandles
        {
            get
            {
                return showScaleHandles;
            }
            set
            {
                if (showScaleHandles != value)
                {
                    showScaleHandles = value;
                    ResetHandleVisibility();
                }
            }
        }
        [SerializeField]
        private bool showRotateHandles = true;
        public bool ShowRotateHandles
        {
            get
            {
                return showRotateHandles;
            }
            set
            {
                if (showRotateHandles != value)
                {
                    showRotateHandles = value;
                    ResetHandleVisibility();
                }
            }
        }
        [SerializeField]
        private float linkRadius = 0.005f;
        [SerializeField]
        private float ballRadius = 0.035f;
        [SerializeField]
        private float cornerRadius = 0.03f;
        #endregion Serialized Fields

        private bool active = false;
        public bool Active
        {
            get
            {
                return active;
            }
            set
            {
                if (active != value)
                {
                    if (value == true)
                    {
                        CreateRig();
                        rigRoot.SetActive(true);
                    }
                    else
                    {
                        DestroyRig();
                    }

                    active = value;
                }
            }
        }

        #region Constants
        private const int LTB = 0;
        private const int LTF = 1;
        private const int LBF = 2;
        private const int LBB = 3;
        private const int RTB = 4;
        private const int RTF = 5;
        private const int RBF = 6;
        private const int RBB = 7;
        private const int cornerCount = 8;
        #endregion Constants

        #region Private Properties
        private Vector3 grabStartPoint;
        private IMixedRealityPointer currentPointer;
        private IMixedRealityInputSource currentInputSource;
        private Vector3 initialGazePoint = Vector3.zero;
        private GameObject targetObject;
        private GameObject rigRoot;
        private BoxCollider cachedTargetCollider;
        private Vector3[] boundsCorners;
        private Vector3 currentBoundsSize;
        private BoundsCalculationMethod boundsMethod;
        private HandleMoveType handleMoveType = HandleMoveType.Point;

        private List<GameObject> links;
        private List<GameObject> corners;
        private List<GameObject> balls;
        private Vector3 defaultScale;
        private List<Renderer> cornerRenderers;
        private List<Renderer> ballRenderers;
        private List<Renderer> linkRenderers;
        private List<Collider> cornerColliders;
        private List<Collider> ballColliders;
        private Vector3[] edgeCenters;

        private Ray initialGrabRay;
        private Ray currentGrabRay;
        private float initialGrabMag;
        private Vector3 currentRotationAxis;
        private Vector3 initialScale;
        private Vector3 initialGrabbedPosition;
        private Vector3 initialGrabbedCentroid;
        private Vector3 initialGrabPoint;

        private CardinalAxisType[] edgeAxes;
        private int[] flattenedHandles;
        private Vector3 boundsCentroid;
        private GameObject grabbedHandle;
        private bool usingPose = false;
        private Vector3 currentPosePosition = Vector3.zero;

        private HandleType currentHandleType;
        private Vector3 lastBounds;
        #endregion Private Properties

        #region Monobehaviour Methods
        private void Start()
        {
            targetObject = this.gameObject;

            if (MixedRealityManager.IsInitialized && MixedRealityManager.InputSystem != null )
            {
                MixedRealityManager.InputSystem.Register(targetObject);
            }

            if (activateOnStart == true)
            {
                Active = true;
            }
        }
        private void Update()
        {
            if (currentInputSource == null)
            {
               UpdateBounds();
            }
            else
            {
                UpdateBounds();
                TransformRig();
            }

            UpdateRigHandles();
        }
        #endregion Monobehaviour Methods

        #region Private Methods
        private void CreateRig()
        {
            DestroyRig();
            SetMaterials();
            InitializeDataStructures();

            SetBoundingBoxCollider();

            UpdateBounds();
            AddCorners();
            AddLinks();
            UpdateRigHandles();
            Flatten();
            ResetHandleVisibility();
            rigRoot.SetActive(false);
        }
        private void DestroyRig()
        {
            if (BoxColliderToUse == null)
            {
                Destroy(cachedTargetCollider);
            }
            else
            {
                BoxColliderToUse.size -= wireframePadding;
            }

            if (balls != null)
            {
                foreach (GameObject gameObject in balls)
                {
                    Object.Destroy(gameObject);
                }
                balls.Clear();
            }

            if (links != null)
            {
                foreach (GameObject gameObject in links)
                {
                    Object.Destroy(gameObject);
                }
                links.Clear();
            }

            if (corners != null)
            {
                foreach (GameObject gameObject in corners)
                {
                    Object.Destroy(gameObject);
                }
                corners.Clear();
            }

            if (rigRoot != null)
            {
                Object.Destroy(rigRoot);
                rigRoot = null;
            }
        }
        private void TransformRig()
        {
            if (usingPose == true)
            {
                TransformHandleWithPoint();
            }
            else
            {
                if (handleMoveType == HandleMoveType.Ray)
                {
                    TransformHandleWithRay();
                }
                else if (handleMoveType == HandleMoveType.Point)
                {
                    TransformHandleWithPoint();
                }
            }
        }
        private void TransformHandleWithRay()
        {
            if (currentHandleType != HandleType.none)
            {
                currentGrabRay = GetHandleGrabbedRay();
                Vector3 grabRayPt = currentGrabRay.origin + (currentGrabRay.direction * initialGrabMag);

                if (currentHandleType == HandleType.rotation)
                {
                    RotateByHandle(grabRayPt);
                }
                else if (currentHandleType == HandleType.scale)
                {
                    ScaleByHandle(grabRayPt);
                }
            }
        }
        private void TransformHandleWithPoint()
        {
            if (currentHandleType != HandleType.none)
            {
                Vector3 newGrabbedPosition;
                Vector3 newRemotePoint;
                //TODO: this line gets the finger grab point in space in hololens
                if (usingPose == false)
                {
                    currentPointer.TryGetPointerPosition(out newRemotePoint);
                    newGrabbedPosition = initialGrabbedPosition + (newRemotePoint - initialGrabPoint);
                }
                else
                {
                    if (initialGazePoint == Vector3.zero)
                    {
                        return;
                    }
                    newGrabbedPosition = currentPosePosition;
                }

                if (currentHandleType == HandleType.rotation)
                {
                    RotateByHandle(newGrabbedPosition);
                }
                else if (currentHandleType == HandleType.scale)
                {
                    ScaleByHandle(newGrabbedPosition);
                }
            }
        }

        private void RotateByHandle(Vector3 newHandlePosition)
        {
            Vector3 projPt = Vector3.ProjectOnPlane((newHandlePosition - rigRoot.transform.position).normalized, currentRotationAxis);
            Quaternion q = Quaternion.FromToRotation((grabbedHandle.transform.position - rigRoot.transform.position).normalized, projPt.normalized);
            Vector3 axis;
            float angle;
            q.ToAngleAxis(out angle, out axis);
            targetObject.transform.RotateAround(rigRoot.transform.position, axis, angle);
        }
        private void ScaleByHandle(Vector3 newHandlePosition)
        {
            bool lockBackCorner = false;
            Vector3 correctedPt = PointToRay(rigRoot.transform.position, grabbedHandle.transform.position, newHandlePosition);
            Vector3 rigCentroid = rigRoot.transform.position;
            float startMag = (initialGrabbedPosition - rigCentroid).magnitude;
            float newMag = (correctedPt - rigCentroid).magnitude;

            if (lockBackCorner == false)
            {
                bool isClamped;
                float ratio = newMag / startMag;
                Vector3 newScale = ClampScale(initialScale * ratio, out isClamped);
                //scale from object center
                targetObject.transform.localScale = newScale;
            }
            else
            {
                bool isClamped;
                float halfRatio = ((newMag + startMag) * 0.5f) / startMag;
                Vector3 newScale = ClampScale(initialScale * halfRatio, out isClamped);
                if (isClamped == false)
                {
                    //scale from object center
                    targetObject.transform.localScale = newScale;
                    Vector3 oldHandlePosition = grabbedHandle.transform.position;
                    UpdateRigHandles();
                    targetObject.transform.position = initialGrabbedCentroid + (grabbedHandle.transform.position - oldHandlePosition);
                }
            }


        }
        private Vector3 GetRotationAxis(GameObject handle)
        {
            for (int i = 0; i < balls.Count; ++i)
            {
                if (handle == balls[i])
                {
                    if (edgeAxes[i] == CardinalAxisType.X)
                    {
                        return rigRoot.transform.right;
                    }
                    else if (edgeAxes[i] == CardinalAxisType.Y)
                    {
                        return rigRoot.transform.up;
                    }
                    else
                    {
                        return rigRoot.transform.forward;
                    }
                }
            }

            return Vector3.zero;
        }
        private void AddCorners()
        {
           for (int i = 0; i < boundsCorners.Length; ++i)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "corner_" + i.ToString();
                //cube.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
                cube.transform.localScale = new Vector3(cornerRadius, cornerRadius, cornerRadius);
                cube.transform.position = boundsCorners[i];

                cube.transform.parent = rigRoot.transform;

                Renderer renderer = cube.GetComponent<Renderer>();
                cornerRenderers.Add(renderer);
                cornerColliders.Add(cube.GetComponent<Collider>());
                corners.Add(cube);

                if (handleMaterial != null)
                {
                   renderer.material = handleMaterial;
                }
            }
        }
        private void AddLinks()
        {
            edgeCenters = new Vector3[12];

            CalculateEdgeCenters();

            Renderer renderer;
            for (int i = 0; i < edgeCenters.Length; ++i)
            {
                GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.name = "midpoint_" + i.ToString();
               // ball.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
                ball.transform.localScale = new Vector3(ballRadius, ballRadius, ballRadius);
                ball.transform.position = edgeCenters[i];

                ball.transform.parent = rigRoot.transform;

                renderer = ball.GetComponent<Renderer>();
                ballRenderers.Add(renderer);
                ballColliders.Add(ball.GetComponent<Collider>());
                balls.Add(ball);

                if (handleMaterial != null)
                {
                    renderer.material = handleMaterial;
                }
            }

            edgeAxes = new CardinalAxisType[12];

            edgeAxes[0] = CardinalAxisType.X;
            edgeAxes[1] = CardinalAxisType.Y;
            edgeAxes[2] = CardinalAxisType.X;
            edgeAxes[3] = CardinalAxisType.Y;
            edgeAxes[4] = CardinalAxisType.X;
            edgeAxes[5] = CardinalAxisType.Y;
            edgeAxes[6] = CardinalAxisType.X;
            edgeAxes[7] = CardinalAxisType.Y;
            edgeAxes[8] = CardinalAxisType.Z;
            edgeAxes[9] = CardinalAxisType.Z;
            edgeAxes[10] = CardinalAxisType.Z;
            edgeAxes[11] = CardinalAxisType.Z;

            GameObject link;
            for (int i = 0; i < edgeCenters.Length; ++i)
            {
                if (wireframeShape == WireframeType.Cubic)
                {
                    link = GameObject.CreatePrimitive(PrimitiveType.Cube);
                }
                else
                {
                    link = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                }
                link.name = "link_" + i.ToString();
               // link.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;

                Vector3 linkDimensions = GetLinkDimensions();
                if (edgeAxes[i] == CardinalAxisType.Y)
                {
                    link.transform.localScale = new Vector3(linkRadius, linkDimensions.y, linkRadius);
                    link.transform.Rotate(new Vector3(0.0f, 90.0f, 0.0f));
                }
                else if (edgeAxes[i] == CardinalAxisType.Z)
                {
                    link.transform.localScale = new Vector3(linkRadius, linkDimensions.z, linkRadius);
                    link.transform.Rotate(new Vector3(90.0f, 0.0f, 0.0f));
                }
                else//X
                {
                    link.transform.localScale = new Vector3(linkRadius, linkDimensions.x, linkRadius);
                    link.transform.Rotate(new Vector3(0.0f, 0.0f, 90.0f));
                }

                link.transform.position = edgeCenters[i];
                link.transform.parent = rigRoot.transform;
                Renderer linkRenderer = link.GetComponent<Renderer>();
                linkRenderers.Add(linkRenderer);

                if (wireframeMaterial != null)
                {
                    linkRenderer.material = wireframeMaterial;
                }

                links.Add(link);
            }
        }
        private void SetBoundingBoxCollider()
        {
            //Collider.bounds is world space bounding volume.
            //Mesh.bounds is local space bounding volume
            //Renderer.bounds is the same as mesh.bounds but in world space coords

            if (BoxColliderToUse != null)
            {
                cachedTargetCollider = BoxColliderToUse;
            }
            else
            {
                Bounds bounds = GetTargetBounds();
                cachedTargetCollider = targetObject.AddComponent<BoxCollider>();
                if (boundsMethod == BoundsCalculationMethod.Renderers)
                {
                    cachedTargetCollider.center = bounds.center;
                    cachedTargetCollider.size = bounds.size;
                }
                else if (boundsMethod == BoundsCalculationMethod.Colliders)
                {
                    cachedTargetCollider.center = bounds.center;
                    cachedTargetCollider.size = bounds.size;
                }
            }

            cachedTargetCollider.size += wireframePadding;
        }
        private Bounds GetTargetBounds()
        {
            Bounds bounds = new Bounds();

            if (targetObject.transform.childCount == 0)
            {
                bounds = GetSingleObjectBounds(targetObject);
                boundsMethod = BoundsCalculationMethod.Collider;
                return bounds;
            }
            else
            {
                for (int i = 0; i < targetObject.transform.childCount; ++i)
                {
                    if (bounds.size == Vector3.zero)
                    {
                        bounds = GetSingleObjectBounds(targetObject.transform.GetChild(i).gameObject);
                    }
                    else
                    {
                        Bounds childBounds = GetSingleObjectBounds(targetObject.transform.GetChild(i).gameObject);
                        if (childBounds.size != Vector3.zero)
                        {
                            bounds.Encapsulate(childBounds);
                        }
                    }
                }

                if (bounds.size != Vector3.zero)
                {
                    boundsMethod = BoundsCalculationMethod.Colliders;
                    return bounds;
                }
            }
            
            //simple case: sum of existing colliders
            Collider[] colliders = targetObject.GetComponentsInChildren<Collider>();
            if (colliders.Length > 0)
            {
                //Collider.bounds is in world space.
                bounds = colliders[0].bounds;
                for (int i = 0; i < colliders.Length; ++i)
                {
                    if (colliders[i].bounds.size != Vector3.zero)
                    {
                        bounds.Encapsulate(colliders[i].bounds);
                    }
                }
                if (bounds.size != Vector3.zero)
                {
                    boundsMethod = BoundsCalculationMethod.Colliders;
                    return bounds;
                }
            }

            //Renderer bounds is local. Requires transform to global coord system.
            Renderer[] childRenderers = targetObject.GetComponentsInChildren<Renderer>();
            if (childRenderers.Length > 0)
            {
                bounds = new Bounds();
                bounds = childRenderers[0].bounds;
                Vector3[] corners = new Vector3[cornerCount];
                for (int i = 0; i < childRenderers.Length; ++i)
                {
                    bounds.Encapsulate(childRenderers[i].bounds);
                }

                GetCornerPositionsFromBounds(bounds,  ref boundsCorners);         
                for (int c = 0; c < corners.Length; ++c)
                {
                    GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = c.ToString();
                    cube.transform.localScale = new Vector3(0.02f, 0.02f, 0.02f);
                    cube.transform.position = boundsCorners[c];
                }

                boundsMethod = BoundsCalculationMethod.Renderers;
                return bounds;
            }

            MeshFilter[] meshFilters = targetObject.GetComponentsInChildren<MeshFilter>();
            if (meshFilters.Length > 0)
            {
                //Mesh.bounds is local space bounding volume
                bounds.size = meshFilters[0].mesh.bounds.size;
                bounds.center = meshFilters[0].mesh.bounds.center;
                for (int i = 0; i < meshFilters.Length; ++i)
                {
                    bounds.Encapsulate(meshFilters[i].mesh.bounds);
                }
                if (bounds.size != Vector3.zero)
                {
                    bounds.center = targetObject.transform.position;
                    boundsMethod = BoundsCalculationMethod.MeshFilters;
                    return bounds;
                }
            }

            BoxCollider boxCollider = targetObject.AddComponent<BoxCollider>();
            bounds = boxCollider.bounds;
            Destroy(boxCollider);
            boundsMethod = BoundsCalculationMethod.Collider;
            return bounds;
        }
        private Bounds GetSingleObjectBounds(GameObject gameObject)
        {
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            Component[] components = gameObject.GetComponents<Component>();
            if (components.Length < 3)
            {
                return bounds;
            }
            BoxCollider boxCollider;
            boxCollider = gameObject.GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                boxCollider = gameObject.AddComponent<BoxCollider>();
                bounds = boxCollider.bounds;
                DestroyImmediate(boxCollider);
            }
            else
            {
                bounds = boxCollider.bounds;
            }

            return bounds;
        }
        private void SetMaterials()
        {
            //ensure materials
            if (wireframeMaterial == null)
            {
                float[] color = { 1.0f, 1.0f, 1.0f, 0.75f };

                Shader.EnableKeyword("_InnerGlow");
                Shader shader = Shader.Find("Mixed Reality Toolkit/Standard");

                wireframeMaterial = new Material(shader);
                wireframeMaterial.SetColor("_Color", new Color(0.0f, 0.63f, 1.0f));
                wireframeMaterial.SetFloat("_InnerGlow", 1.0f);
                wireframeMaterial.SetFloatArray("_InnerGlowColor", color);
            }
            if (handleMaterial == null && handleMaterial != wireframeMaterial)
            {
                float[] color = { 1.0f, 1.0f, 1.0f, 0.75f };

                Shader.EnableKeyword("_InnerGlow");
                Shader shader = Shader.Find("Mixed Reality Toolkit/Standard");

                handleMaterial = new Material(shader);
                handleMaterial.SetColor("_Color", new Color(0.0f, 0.63f, 1.0f));
                handleMaterial.SetFloat("_InnerGlow", 1.0f);
                handleMaterial.SetFloatArray("_InnerGlowColor", color);
            }
            if (handleGrabbedMaterial == null && handleGrabbedMaterial != handleMaterial && handleGrabbedMaterial != wireframeMaterial)
            {
                float[] color = { 1.0f, 1.0f, 1.0f, 0.75f };

                Shader.EnableKeyword("_InnerGlow");
                Shader shader = Shader.Find("Mixed Reality Toolkit/Standard");

                handleGrabbedMaterial = new Material(shader);
                handleGrabbedMaterial.SetColor("_Color", new Color(0.0f, 0.63f, 1.0f));
                handleGrabbedMaterial.SetFloat("_InnerGlow", 1.0f);
                handleGrabbedMaterial.SetFloatArray("_InnerGlowColor", color);
            }
        }
        private void InitializeDataStructures()
        {
            rigRoot = new GameObject("rigRoot");
            rigRoot.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;

            boundsCorners = new Vector3[8];
            defaultScale = targetObject.transform.localScale;

            corners = new List<GameObject>();
            cornerColliders = new List<Collider>();
            cornerRenderers = new List<Renderer>();
            balls = new List<GameObject>();
            ballRenderers = new List<Renderer>();
            ballColliders = new List<Collider>();
            links = new List<GameObject>();
            linkRenderers = new List<Renderer>();
        }
        private void CalculateEdgeCenters()
        {
            if (boundsCorners != null && edgeCenters != null)
            {
                edgeCenters[0] = (boundsCorners[0] + boundsCorners[1]) * 0.5f;
                edgeCenters[1] = (boundsCorners[1] + boundsCorners[2]) * 0.5f;
                edgeCenters[2] = (boundsCorners[2] + boundsCorners[3]) * 0.5f;
                edgeCenters[3] = (boundsCorners[3] + boundsCorners[0]) * 0.5f;

                edgeCenters[4] = (boundsCorners[4] + boundsCorners[5]) * 0.5f;
                edgeCenters[5] = (boundsCorners[5] + boundsCorners[6]) * 0.5f;
                edgeCenters[6] = (boundsCorners[6] + boundsCorners[7]) * 0.5f;
                edgeCenters[7] = (boundsCorners[7] + boundsCorners[4]) * 0.5f;

                edgeCenters[8] = (boundsCorners[0] + boundsCorners[4]) * 0.5f;
                edgeCenters[9] = (boundsCorners[1] + boundsCorners[5]) * 0.5f;
                edgeCenters[10] = (boundsCorners[2] + boundsCorners[6]) * 0.5f;
                edgeCenters[11] = (boundsCorners[3] + boundsCorners[7]) * 0.5f;
            }
        }
        private Vector3 ClampScale(Vector3 scale, out bool clamped)
        {
            Vector3 finalScale = scale;
            Vector3 maximumScale = initialScale * scaleMaximum;
            clamped = false;

            if (scale.x > maximumScale.x || scale.y > maximumScale.y || scale.z > maximumScale.z)
            {
                finalScale = maximumScale;
                clamped = true;
            }

            Vector3 minimumScale = initialScale * scaleMinimum;

            if (finalScale.x < minimumScale.x || finalScale.y < minimumScale.y || finalScale.z < minimumScale.z)
            {
                finalScale = minimumScale;
                clamped = true;
            }

            return finalScale;
        }
        private Vector3 GetLinkDimensions()
        {
            float linkLengthAdjustor = wireframeShape == WireframeType.Cubic ? 2.0f : 1.0f - (6.0f * linkRadius);
            return (currentBoundsSize * linkLengthAdjustor) + new Vector3(linkRadius, linkRadius, linkRadius);
        }
        private void ResetHandleVisibility()
        {
            bool isVisible;

            //set balls visibility
            if (balls != null)
            {
                isVisible = (wireframeOnly ? false : showRotateHandles);
                for (int i = 0; i < ballRenderers.Count; ++i)
                {
                    ballRenderers[i].material = handleMaterial;
                    ballRenderers[i].enabled = isVisible;
                }
            }

            //set corner visibility
            if (corners != null)
            {
                isVisible = (wireframeOnly ? false : showScaleHandles);
                for (int i = 0; i < cornerRenderers.Count; ++i)
                {
                    cornerRenderers[i].material = handleMaterial;
                    cornerRenderers[i].enabled = isVisible;
                }
            }

            SetHiddenHandles();
        }
        private void ShowOneHandle(GameObject handle)
        {
            //turn off all balls
            if (balls != null)
            {
                for (int i = 0; i < ballRenderers.Count; ++i)
                {
                    ballRenderers[i].enabled = false;
                }
            }

            //turn off all corners
            if (corners != null)
            {
                for (int i = 0; i < cornerRenderers.Count; ++i)
                {
                    cornerRenderers[i].enabled = false;
                }
            }

            //turn on one handle
            if (handle != null)
            {
                Renderer r = handle.GetComponent<Renderer>();
                r.material = handleGrabbedMaterial;
                r.enabled = true;
            }
        }
        private void UpdateBounds()
        {
            Vector3 boundsSize = Vector3.zero;
            Vector3 centroid = Vector3.zero;

            //store current rotation then zero out the rotation so that the bounds
            //are computed when the object is in its 'axis aligned orientation'.
            Quaternion currentRotation = targetObject.transform.rotation;
            targetObject.transform.rotation = Quaternion.identity;

            if (cachedTargetCollider != null)
            {
                boundsSize = cachedTargetCollider.bounds.extents;
                centroid = cachedTargetCollider.bounds.center;
            }
           
            //after bounds are computed, restore rotation...
            targetObject.transform.rotation = currentRotation;

            if (boundsSize != Vector3.zero)
            {
                if (flattenAxis == FlattenModeType.FlattenAuto)
                {
                    float min = Mathf.Min(boundsSize.x, Mathf.Min(boundsSize.y, boundsSize.z));
                    flattenAxis = min == boundsSize.x ? FlattenModeType.FlattenX : (min == boundsSize.y ? FlattenModeType.FlattenY : FlattenModeType.FlattenZ);
                }

                boundsSize.x = flattenAxis == FlattenModeType.FlattenX ? 0.0f : boundsSize.x;
                boundsSize.y = flattenAxis == FlattenModeType.FlattenY ? 0.0f : boundsSize.y;
                boundsSize.z = flattenAxis == FlattenModeType.FlattenZ ? 0.0f : boundsSize.z;

                currentBoundsSize = boundsSize;
                boundsCentroid = centroid;

                boundsCorners[0] = centroid - new Vector3(centroid.x - currentBoundsSize.x, centroid.y - currentBoundsSize.y, centroid.z - currentBoundsSize.z);
                boundsCorners[1] = centroid - new Vector3(centroid.x + currentBoundsSize.x, centroid.y - currentBoundsSize.y, centroid.z - currentBoundsSize.z);
                boundsCorners[2] = centroid - new Vector3(centroid.x + currentBoundsSize.x, centroid.y + currentBoundsSize.y, centroid.z - currentBoundsSize.z);
                boundsCorners[3] = centroid - new Vector3(centroid.x - currentBoundsSize.x, centroid.y + currentBoundsSize.y, centroid.z - currentBoundsSize.z);

                boundsCorners[4] = centroid - new Vector3(centroid.x - currentBoundsSize.x, centroid.y - currentBoundsSize.y, centroid.z + currentBoundsSize.z);
                boundsCorners[5] = centroid - new Vector3(centroid.x + currentBoundsSize.x, centroid.y - currentBoundsSize.y, centroid.z + currentBoundsSize.z);
                boundsCorners[6] = centroid - new Vector3(centroid.x + currentBoundsSize.x, centroid.y + currentBoundsSize.y, centroid.z + currentBoundsSize.z);
                boundsCorners[7] = centroid - new Vector3(centroid.x - currentBoundsSize.x, centroid.y + currentBoundsSize.y, centroid.z + currentBoundsSize.z);

                CalculateEdgeCenters();
            }
        }
        private void UpdateRigHandles()
        {
            if (rigRoot != null && targetObject != null)
            {
                rigRoot.transform.rotation = Quaternion.identity;
                rigRoot.transform.position = Vector3.zero;

                for (int i = 0; i < corners.Count; ++i)
                {
                    corners[i].transform.position = boundsCorners[i];
                }

                Vector3 linkDimensions = GetLinkDimensions();

                for (int i = 0; i < edgeCenters.Length; ++i)
                {
                    balls[i].transform.position = edgeCenters[i];
                    links[i].transform.position = edgeCenters[i];

                    if (edgeAxes[i] == CardinalAxisType.X)
                    {
                        links[i].transform.localScale = new Vector3(linkRadius, linkDimensions.x, linkRadius);
                    }
                    else if (edgeAxes[i] == CardinalAxisType.Y)
                    {
                        links[i].transform.localScale = new Vector3(linkRadius, linkDimensions.y, linkRadius);
                    }
                    else//Z
                    {
                        links[i].transform.localScale = new Vector3(linkRadius, linkDimensions.z, linkRadius);
                    }
                }

                //move rig into position and rotation
                rigRoot.transform.position = cachedTargetCollider.bounds.center;
                rigRoot.transform.rotation = targetObject.transform.rotation;
            }
        }
        private HandleType GetHandleType(GameObject handle)
        {
            for (int i = 0; i < balls.Count; ++i)
            {
                if (handle == balls[i])
                {
                    return HandleType.rotation;
                }
            }
            for (int i = 0; i < corners.Count; ++i)
            {
                if (handle == corners[i])
                {
                    return HandleType.scale;
                }
            }

            return HandleType.none;
        }
        private Collider GetGrabbedCollider(Ray ray, out float distance)
        {
            Collider closestCollider = null;
            float currentDistance;
            float closestDistance = float.MaxValue;


            for (int i = 0; i < cornerColliders.Count; ++i)
            {
                if (cornerRenderers[i].enabled == true && true == cornerColliders[i].bounds.IntersectRay(ray, out currentDistance))
                {
                    if (currentDistance < closestDistance)
                    {
                        closestDistance = currentDistance;
                        closestCollider = cornerColliders[i];
                    }
                }
            }

            for (int i = 0; i < ballColliders.Count; ++i)
            {
                if (ballRenderers[i].enabled == true && true == ballColliders[i].bounds.IntersectRay(ray, out currentDistance))
                {
                    if (currentDistance < closestDistance)
                    {
                        closestDistance = currentDistance;
                        closestCollider = ballColliders[i];
                    }
                }
            }

            distance = closestDistance;
            return closestCollider;
        }
        private Ray GetHandleGrabbedRay()
        {
            Ray pointerRay = new Ray();
            if (currentInputSource.Pointers.Length > 0)
            {
               currentInputSource.Pointers[0].TryGetPointingRay(out pointerRay);
            }

            return pointerRay;
        }
        private void Flatten()
        {
            if (flattenAxis == FlattenModeType.FlattenX)
            {
                flattenedHandles = new int[] { 0, 4, 2, 6 };
            }
            else if (flattenAxis == FlattenModeType.FlattenY)
            {
                flattenedHandles = new int[] { 1, 3, 5, 7 };
            }
            else if (flattenAxis == FlattenModeType.FlattenZ)
            {
                flattenedHandles = new int[] { 9, 10, 8, 11 };
            }

            if (flattenedHandles != null)
            {
                for (int i = 0; i < flattenedHandles.Length; ++i)
                {
                    linkRenderers[flattenedHandles[i]].enabled = false;
                }
            }
        }
        private void SetHiddenHandles()
        {
            if (flattenedHandles != null)
            {
                for (int i = 0; i < flattenedHandles.Length; ++i)
                {
                    ballRenderers[flattenedHandles[i]].enabled = false;
                }
            }
        }
        private void GetCornerPositionsFromBounds(Bounds bounds, ref Vector3[] positions)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            float leftEdge = center.x - extents.x;
            float rightEdge = center.x + extents.x;
            float bottomEdge = center.y - extents.y;
            float topEdge = center.y + extents.y;
            float frontEdge = center.z - extents.z;
            float backEdge = center.z + extents.z;

            if (positions == null || positions.Length != cornerCount)
            {
                positions = new Vector3[cornerCount];
            }

            positions[LBF] = new Vector3(leftEdge, bottomEdge, frontEdge);
            positions[LBB] = new Vector3(leftEdge, bottomEdge, backEdge);
            positions[LTF] = new Vector3(leftEdge, topEdge, frontEdge);
            positions[LTB] = new Vector3(leftEdge, topEdge, backEdge);
            positions[RBF] = new Vector3(rightEdge, bottomEdge, frontEdge);
            positions[RBB] = new Vector3(rightEdge, bottomEdge, backEdge);
            positions[RTF] = new Vector3(rightEdge, topEdge, frontEdge);
            positions[RTB] = new Vector3(rightEdge, topEdge, backEdge);
        }
        private static Vector3 PointToRay(Vector3 origin, Vector3 end, Vector3 closestPoint)
        {
            Vector3 originToPoint = closestPoint - origin;       
            Vector3 originToEnd = end - origin;            
            float magnitudeAB = originToEnd.sqrMagnitude;     
            float dotProduct = Vector3.Dot(originToPoint, originToEnd);   
            float distance = dotProduct / magnitudeAB; 
            return origin + (originToEnd * distance);
        }
        private static Vector3 GetSizeFromBoundsCorners(Vector3[] corners)
        {
            return new Vector3(Mathf.Abs(corners[0].x - corners[1].x),
                                Mathf.Abs(corners[0].y - corners[3].y),
                                Mathf.Abs(corners[0].z - corners[4].z));
        }
        private static Vector3 GetCenterFromBoundsCorners(Vector3[] corners)
        {
            Vector3 center = Vector3.zero;
            for (int i = 0; i < corners.Length; i++)
            {
                center += corners[i];
            }
            center *= (1.0f / (float)corners.Length);
            return center;
        }
        #endregion Private Methods

        #region Used Event Handlers
        public void OnPointerDown(MixedRealityPointerEventData eventData)
        {
            //Debug.Log("pointerDown");
            //if (currentInputSource == null)
            //{
            //    if (eventData.Pointer is Pointers.LinePointer == true)
            //    {
            //        Ray ray;
            //        if (true == eventData.Pointer.TryGetPointingRay(out ray))
            //        {
            //            handleMoveType = HandleMoveType.Ray;
            //            float distance = 0;
            //            Collider collider = GetGrabbedCollider(ray, out distance);
            //            if (collider != null)
            //            {
            //                currentInputSource = eventData.InputSource;
            //                currentPointer = eventData.Pointer;
            //                grabbedHandle = collider.gameObject;
            //                currentHandleType = GetHandleType(grabbedHandle);
            //                currentRotationAxis = GetRotationAxis(grabbedHandle);
            //                currentPointer.TryGetPointingRay(out initialGrabRay);
            //                initialGrabMag = distance;
            //                initialGrabbedPosition = grabbedHandle.transform.position;
            //                initialScale = targetObject.transform.localScale;
            //                ShowOneHandle(grabbedHandle);
            //            }
            //        }
            //    }
            //}
        }
        public void OnInputDown(InputEventData eventData)
        {
            if (currentInputSource == null)
            {
                IMixedRealityPointer pointer = eventData.InputSource.Pointers[0];
                Ray ray;
                if (true == pointer.TryGetPointingRay(out ray))
                {
                    handleMoveType = HandleMoveType.Ray;
                    float distance = 0;
                    Collider collider = GetGrabbedCollider(ray, out distance);
                    if (collider != null)
                    {
                        currentInputSource = eventData.InputSource;

                        currentPointer = pointer;
                        grabbedHandle = collider.gameObject;
                        currentHandleType = GetHandleType(grabbedHandle);
                        currentRotationAxis = GetRotationAxis(grabbedHandle);
                        currentPointer.TryGetPointingRay(out initialGrabRay);
                        initialGrabMag = distance;
                        initialGrabbedPosition = grabbedHandle.transform.position;
                        initialGrabbedCentroid = targetObject.transform.position;
                        initialScale = targetObject.transform.localScale;
                        pointer.TryGetPointerPosition(out initialGrabPoint);
                        ShowOneHandle(grabbedHandle);
                        initialGazePoint = Vector3.zero;
                    }
                }
            }
        }
        public void OnInputUp(InputEventData eventData)
        {
            if (currentInputSource == eventData.InputSource)
            {
                currentInputSource = null;
                currentHandleType = HandleType.none;
                currentPointer = null;
                grabbedHandle = null;
                ResetHandleVisibility();
            }
        }
        public void OnPoseInputChanged(InputEventData<MixedRealityPose> eventData)
        {
            if (currentInputSource != null && eventData.InputSource == currentInputSource)
            {
                if (eventData.InputSource.SourceName.Contains("Hand"))
                {
                    usingPose = true;
                    if (initialGazePoint == Vector3.zero)
                    {
                        initialGazePoint = eventData.InputData.Position;
                    }
                    currentPosePosition = initialGrabbedPosition + (eventData.InputData.Position - initialGazePoint);
                }
            }
            else
            {
                usingPose = false;
            }
        }
        #endregion Used Event Handlers

        #region Unused Event Handlers
        public void OnPointerUp(MixedRealityPointerEventData eventData)
        {
        }
        public void OnPointerClicked(MixedRealityPointerEventData eventData)
        {
        }   
        public void OnInputPressed(InputEventData<float> eventData)
        {
        }
        public void OnPositionInputChanged(InputEventData<Vector2> eventData)
        {
        }

        public void OnGestureStarted(InputEventData eventData)
        {
        }

        public void OnGestureUpdated(InputEventData eventData)
        {
        }

        public void OnGestureCompleted(InputEventData eventData)
        {
        }

        public void OnGestureCanceled(InputEventData eventData)
        {
        }

        public void OnPositionChanged(InputEventData<Vector3> eventData)
        {
        }

        public void OnRotationChanged(InputEventData<Quaternion> eventData)
        {
        }

        public void OnSourceDetected(SourceStateEventData eventData)
        {
        }

        public void OnSourceLost(SourceStateEventData eventData)
        {
            if (currentInputSource == eventData.InputSource)
            {
                currentInputSource = null;
                currentHandleType = HandleType.none;
                currentPointer = null;
                grabbedHandle = null;
                ResetHandleVisibility();
            }
        }


        #endregion Unused Event Handlers
    }
}
