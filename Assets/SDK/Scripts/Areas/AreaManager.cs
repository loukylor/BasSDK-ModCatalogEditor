using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#else
using EasyButtons;
#endif

namespace ThunderRoad
{
    public class AreaManager : ThunderBehaviour
    {
        #region Static
        public static AreaManager Instance = null;
        #endregion Static

        #region Fields

        private SpawnableArea _currentArea = null;
#if ODIN_INSPECTOR
        [ShowInInspector]
#endif
        private List<SpawnableArea> _currentTree = null;
#if ODIN_INSPECTOR
        [ShowInInspector]
#endif
        private List<int> _currentTreeDepthIndex = null;
#if ODIN_INSPECTOR
        [ShowInInspector]
#endif
        private int _areaSpawnDepth = 100;
#if ODIN_INSPECTOR
        [ShowInInspector]
#endif
        private int _areaDespawnDepth = 100;
#if ODIN_INSPECTOR
        [ShowInInspector]
#endif
        private int _areaCullDepth = 2;

#if ODIN_INSPECTOR
        [ShowInInspector]
#endif
        private int _areaUniqueId = 0;

        private Transform _playerHeadTransform = null;
        #endregion Fields

        #region Properties
        public override ManagedLoops EnabledManagedLoops => ManagedLoops.Update;
        public int AreaSpawnDepth { get { return _areaSpawnDepth; } set { _areaSpawnDepth = value; } }
        public int AreaDespawnDepth { get { return _areaDespawnDepth; } set { _areaDespawnDepth = value; } }
        public int AreaCullDepth { get { return _areaCullDepth; } set { _areaCullDepth = value; } }

        public SpawnableArea CurrentArea { get { return _currentArea; } }
        public List<SpawnableArea> CurrentTree { get { return _currentTree; } }
        public List<int> CurrentTreeDepthIndex { get { return _currentTreeDepthIndex; } }

        public bool initialized;

        #endregion Properties

        #region Events
        public delegate void PlayerAreaChangeEvent(SpawnableArea newArea, SpawnableArea previousArea);
        public event PlayerAreaChangeEvent OnPlayerChangeAreaEvent;

        public delegate void InitializedEvent(EventTime eventTime);
        public event InitializedEvent OnInitializedEvent;
        #endregion

        #region Methods
        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            DespawnAllAreas();
            Instance = null;
        }

        public IEnumerator Init(SpawnableArea root)
        {
            OnInitializedEvent?.Invoke(EventTime.OnStart);
            initialized = false;
            DespawnAllAreas();
            _currentArea = root;
            _currentTree = root.GetBreadthTree(out _currentTreeDepthIndex);
            int treeCount = _currentTree.Count;
            for (int i = 0; i < treeCount; i++)
            {
                RegisterArea(_currentTree[i]);
            }


            // Spawn/Cull Area
            int indexMaxToSpawn = _areaSpawnDepth - 1 < _currentTreeDepthIndex.Count ? _currentTreeDepthIndex[_areaSpawnDepth - 1] : treeCount;
            for (int i = 0; i < treeCount; i++)
            {
                _currentTree[i].IsCulled = true;

                if (i <= indexMaxToSpawn)
                {
                    yield return _currentTree[i].SpawnCoroutine(true);
                }
            }

            for (int i = 0; i < indexMaxToSpawn; i++)
            {
                while (!_currentTree[i].SpawnedArea.initialized)
                {
                    yield return null;
                }
            }

            _currentArea.IsCulled = false;

            // Activate only the first player spawner
            Area currentArea = _currentArea.SpawnedArea;
            PlayerSpawner initialPLayerSpawner = currentArea.GetPlayerSpawner();
            if(initialPLayerSpawner != null)
            {
                initialPLayerSpawner.gameObject.SetActive(true);
            }
            else
            {
                Debug.LogError("No player spawner in area : " + _currentArea.AreaDataId);

                AreaGateway areaGateway = null;
                foreach (KeyValuePair<int,AreaGateway> pair in currentArea.connectedGateways)
                {
                    AreaRotationHelper.Face face;
                    if (AreaRotationHelper.TryGetFaceFromQuaterion(pair.Value.transform.rotation, out face))
                    {
                        if(face != AreaRotationHelper.Face.Up && face != AreaRotationHelper.Face.Down)
                        {
                            areaGateway = pair.Value;
                            break;
                        }
                    }
                }

                if (areaGateway == null)
                {
                    Debug.LogError("Area root : " + _currentArea.AreaDataId + " has no player spawner or gate not vertical,  player can not spawn");
                    yield break;
                }

                Vector3 spawnPosition = areaGateway.transform.position;
                spawnPosition -= areaGateway.transform.forward * 1.0f;
                Quaternion spawnRotation = areaGateway.transform.rotation * Quaternion.Euler(0,180,0);

                GameObject playerSpawnerGo = Instantiate(new GameObject("PlayerSpawner"), spawnPosition, spawnRotation, currentArea.transform);
                playerSpawnerGo.AddComponent<PlayerSpawner>();                
            }

            initialized = true;
            OnInitializedEvent?.Invoke(EventTime.OnEnd);
        }

        public void RegisterArea(SpawnableArea spawnableArea)
        {
            if (spawnableArea == null) return;
            if (spawnableArea.managedId > 0) return;

            spawnableArea.managedId = _areaUniqueId++;
        }

        public bool TryGetArea(string areaId, out SpawnableArea spawnableArea)
        {
            int count = _currentTree.Count;
            for (int i = 0; i < count; i++)
            {
                if (_currentTree[i].AreaDataId.Equals(areaId))
                {
                    spawnableArea = _currentTree[i];
                    return true;
                }
            }

            spawnableArea = null;
            return false;
        }

        public bool TryGetArea(int areaManagedId, out SpawnableArea spawnableArea)
        {
            int count = _currentTree.Count;
            for (int i = 0; i < count; i++)
            {
                if (_currentTree[i].managedId == areaManagedId)
                {
                    spawnableArea = _currentTree[i];
                    return true;
                }
            }

            spawnableArea = null;
            return false;
        }

        private void DespawnAllAreas()
        {
            if (_currentTree != null)
            {
                int count = _currentTree.Count;
                for (int i = 0; i < count; i++)
                {
                    _currentTree[i].Unload();
                }
            }
        }


        protected internal override void ManagedUpdate()
        {
        }

        private void OnPlayerChangeArea(SpawnableArea newArea)
        {
            SpawnableArea previousArea = _currentArea;
            if (_currentArea != newArea)
            {
                _currentArea.OnPlayerExit(newArea);
                _currentArea = newArea;
                _currentTree = _currentArea.GetBreadthTree(out _currentTreeDepthIndex);
            }

            _currentArea.OnPlayerEnter(previousArea);

            string log = "[AreaManager] : Player Enter : " + _currentArea.AreaDataId;
            if (previousArea != null) log += " And Leave : " + previousArea.AreaDataId;
            Debug.Log(log);
            OnPlayerChangeAreaEvent?.Invoke(_currentArea, previousArea);

            if (_currentTree == null)
            {
                return;
            }

            // Spawn/Despawn/Cull Area
            int treeCount = _currentTree.Count;
            int indexMaxToSpawn = _areaSpawnDepth - 1 < _currentTreeDepthIndex.Count ? _currentTreeDepthIndex[_areaSpawnDepth - 1] : treeCount;
            int indexMinToDeSpawn = _areaDespawnDepth - 1 < _currentTreeDepthIndex.Count ? _currentTreeDepthIndex[_areaDespawnDepth - 1] : treeCount;
            int indexToCull = _areaCullDepth - 1 < _currentTreeDepthIndex.Count ? _currentTreeDepthIndex[_areaCullDepth - 1] : treeCount;
            for (int i = 0; i < treeCount; i++)
            {
                _currentTree[i].IsCulled = i >= indexToCull;

                if (i <= indexMaxToSpawn)
                {
                    _currentTree[i].Spawn(false);
                }

                if (i >= indexMinToDeSpawn)
                {
                    _currentTree[i].Unload();
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (_currentTree == null)
            {
                return;
            }

            int depth = 0;
            for (int i = 0; i < _currentTree.Count; i++)
            {
                SpawnableArea tempArea = _currentTree[i];
                if (_currentTreeDepthIndex.Count > depth
                    && _currentTreeDepthIndex[depth] == i)
                {
                    depth++;
                }

                Gizmos.color = (depth % 2) == 0 ? Color.blue : Color.red;
                Gizmos.DrawWireCube(tempArea.Bounds.center, tempArea.Bounds.size);
                Gizmos.DrawIcon(tempArea.Bounds.center, tempArea.AreaAddress + "_" + tempArea.Rotation, true);
            }
        }
        #endregion Methods

        #region Tools
#if UNITY_EDITOR
        public void EditorSpawn(SpawnableArea root, bool spawnItem)
        {
            _currentArea = root;
            _currentTree = root.GetBreadthTree(out _currentTreeDepthIndex);

            int count = _currentTree.Count;
            for (int i = 0; i < count; i++)
            {
                _currentTree[i].EditorSpawnArea(transform, spawnItem);
            }
        }
#endif //UNITY_EDITOR
        #endregion Tools
    }
}