﻿using System.Collections.Generic;
using CoreSystems.Platform;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace CoreSystems.Support
{
    // based on code of Equinox's
    public class Dummy
    {
        internal bool NullEntity => _entity == null || _entity.MarkedForClose;

        internal MyEntity Entity
        {
            get
            {
                if (_entity?.Model == null) {
                    if (_part.CoreSystem.Session.LocalVersion) Log.Line("reset parts");
                    _part.BaseComp.Platform?.ResetParts();
                    if (_entity?.Model == null && _part.BaseComp.TypeSpecific != CoreComponent.CompTypeSpecific.Phantom)
                        Log.Line("Dummy Entity/Model null");
                }

                return _entity;
            }
            set
            {
                if (value?.Model == null && _part.BaseComp.TypeSpecific != CoreComponent.CompTypeSpecific.Phantom)
                    Log.Line($"DummyModel null for weapon on set: {_part.CoreSystem.PartName}");
                _entity = value; 

            }
        }
        //internal MyEntity Entity ;

        private IMyModel _cachedModel;
        private IMyModel _cachedSubpartModel;
        private MyEntity _cachedSubpart;
        private MatrixD? _cachedDummyMatrix;
        internal Vector3D CachedPos;
        internal Vector3D CachedDir;
        private readonly string[] _path;
        private readonly Dictionary<string, IMyModelDummy> _tmp1 = new Dictionary<string, IMyModelDummy>();
        private readonly Dictionary<string, IMyModelDummy> _tmp2 = new Dictionary<string, IMyModelDummy>();
        private readonly Part _part;
        private MyEntity _entity;
        public Dummy(MyEntity e, Part w, params string[] path)
        {
            _part = w;
            Entity = e;
            _path = path;
        }

        private bool _failed = true;
        internal void UpdateModel()
        {
            _cachedModel = _entity.Model;
            _cachedSubpart = _entity;
            _cachedSubpartModel = _cachedSubpart?.Model;
            for (var i = 0; i < _path.Length - 1; i++)
            {
                MyEntitySubpart part;
                if (_cachedSubpart.TryGetSubpart(_path[i], out part))
                    _cachedSubpart = part;
                else
                {
                    _tmp2.Clear();
                    ((IMyModel)_cachedSubpart.Model)?.GetDummies(_tmp2);
                    _failed = true;
                    return;
                }
            }

            _cachedSubpartModel = _cachedSubpart?.Model;
            _cachedDummyMatrix = null;
            _tmp1.Clear();
            _cachedSubpartModel?.GetDummies(_tmp1);

            IMyModelDummy dummy;
            if (_tmp1.TryGetValue(_path[_path.Length - 1], out dummy))
            {
                _cachedDummyMatrix = MatrixD.Normalize(dummy.Matrix);
                _failed = false;
                return;
            }
            _failed = true;
        }

        internal void UpdatePhantom()
        {
            _cachedSubpart = _entity;
            _cachedDummyMatrix = MatrixD.Identity;
            _failed = false;
        }


        public DummyInfo Info
        {
            get
            {
                if (_part == null || _part.BaseComp.TypeSpecific != CoreComponent.CompTypeSpecific.Phantom) {

                    if (_entity != null && _entity.Model == null && Entity.Model == null)
                        Log.Line("DummyInfo reset and still has invalid enity/model");

                    if (!(_cachedModel == _entity?.Model && _cachedSubpartModel == _cachedSubpart?.Model)) UpdateModel();

                    if (_entity == null || _cachedSubpart == null) {
                        Log.Line("DummyInfo invalid");
                        return new DummyInfo();
                    }
                }
                else
                    UpdatePhantom();


                var dummyMatrix = _cachedDummyMatrix ?? MatrixD.Identity;
                var localPos = dummyMatrix.Translation;
                var localDir = dummyMatrix.Forward;
                CachedPos = Vector3D.Transform(localPos, _cachedSubpart.PositionComp.WorldMatrixRef);
                CachedDir = Vector3D.TransformNormal(localDir, _cachedSubpart.PositionComp.WorldMatrixRef);
                return new DummyInfo { Position = CachedPos, Direction = CachedDir, ParentMatrix = _cachedSubpart.PositionComp.WorldMatrixRef, Entity = _entity, LocalPosition = localPos, DummyMatrix = dummyMatrix};
            }
        }

        public struct DummyInfo
        {
            public Vector3D Position;
            public Vector3D LocalPosition;
            public Vector3D Direction;
            public MatrixD ParentMatrix;
            public MatrixD DummyMatrix;
            public MyEntity Entity;
        }
    }
}
