using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // H1-04: delete confirmation for live and missing entries (split from MmdSceneWorkspace.cs).
    public sealed partial class MmdSceneWorkspace
    {
        internal bool ConfirmVisible { get; private set; }
        private void CreateConfirm()
        {
            // Full-screen pickable overlay: blocks panels and the viewport while open.
            confirm = Element(Root, "eds-overlay"); confirm.name = "RemoveConfirm";
            var box = Element(confirm, "eds-dialog");
            confirmText = Text(box, "", "eds-dialog-text"); confirmText.name = "RemoveConfirmText";
            var row = Element(box, "eds-dialog-buttons");
            TextButton(row, "CancelRemove", "취소", CancelRemove);
            TextButton(row, "ConfirmRemove", "삭제", ConfirmRemove, "eds-button--danger-fill").style.marginRight = 0;
            confirm.style.display = DisplayStyle.None;
        }
        private void RequestRemove()
        {
            if (Busy || (Selected == null && SelectedUnresolved == null)) return;
            var missing = SelectedUnresolved;
            if (missing != null)
            {
                confirmId = SelectedId;
                confirmText.text = "누락된 " + (missing.Data.type == SceneObjectData.StageType ? "배경" : "캐릭터") + " '" + Path.GetFileNameWithoutExtension(missing.Data.assetPath) +
                    "'을(를) 장면에서 삭제할까요?\n저장하면 이 참조가 장면 파일에서 사라집니다. 실행 취소(Ctrl+Z)로 되돌릴 수 있습니다.";
                confirm.style.display = DisplayStyle.Flex; ConfirmVisible = true;
                Root.Q<Button>("CancelRemove").Focus();
                return;
            }
            var item = Selected;
            confirmId = item.Id;
            confirmText.text = (item.Kind == MmdSceneService.ObjectKind.Stage ? "배경" : "캐릭터") + " '" + Path.GetFileNameWithoutExtension(item.ModelSource) +
                "'을(를) 장면에서 삭제할까요?\n원본 파일은 삭제되지 않습니다. 실행 취소(Ctrl+Z)로 되돌릴 수 있습니다.";
            confirm.style.display = DisplayStyle.Flex; ConfirmVisible = true;
            Root.Q<Button>("CancelRemove").Focus();
        }
        private void CancelRemove()
        {
            if (!ConfirmVisible) return;
            confirm.style.display = DisplayStyle.None; ConfirmVisible = false; confirmId = null;
            message = "삭제를 취소했습니다."; Refresh();
        }
        private void ConfirmRemove()
        {
            if (!ConfirmVisible) return;
            var id = confirmId; confirm.style.display = DisplayStyle.None; ConfirmVisible = false; confirmId = null;
            (Root.panel?.focusController?.focusedElement as VisualElement)?.Blur();
            Guard(() =>
            {
                if (RemoveUnresolved(id))
                {
                    if (SelectedId == id) SelectedId = Scene.Objects.FirstOrDefault()?.Id;
                    message = "누락 항목을 장면에서 삭제했습니다.";
                    return;
                }
                var item = Scene.Objects.FirstOrDefault(value => value.Id == id);
                if (item == null) return;
                unresolvedMotions.TryGetValue(id, out var missingMotion);
                var removeCommand = new RemoveCommand { Label = item.Kind == MmdSceneService.ObjectKind.Stage ? "배경 삭제" : "캐릭터 삭제", ObjectId = id, Item = item, Motion = missingMotion };
                removeCommand.Redo(this); Push(removeCommand);
                if (SelectedId == id) SelectedId = Scene.Objects.FirstOrDefault()?.Id;
                message = item.Kind == MmdSceneService.ObjectKind.Stage ? "선택한 배경을 삭제했습니다." : "선택한 캐릭터를 삭제했습니다.";
            });
        }
    }
}
