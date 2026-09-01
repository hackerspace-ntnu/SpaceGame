// MenuStepper touches uGUI (RectTransform, Button, TextMeshProUGUI), so it lives in
// Assembly-CSharp. This test therefore goes in Assets/Game/Editor/Tests/, which compiles into
// Assembly-CSharp-Editor — the only test location that can see Assembly-CSharp types.
// Assets/Game/Tests/EditMode/ has its own asmdef and cannot reference it. BodyOwnershipTests.cs
// carries the same note.
using NUnit.Framework;
using SpaceGame.Presentation;
using UnityEngine;

namespace SpaceGame.Tests
{
    public class MenuStepperTests
    {
        private GameObject parentGo;
        private RectTransform parent;

        [SetUp]
        public void SetUp()
        {
            parentGo = new GameObject("stepper-parent", typeof(RectTransform));
            parent = (RectTransform)parentGo.transform;
        }

        [TearDown]
        public void TearDown()
        {
            if (parentGo != null) Object.DestroyImmediate(parentGo);
        }

        [Test]
        public void ShowsTheValueItWasGiven()
        {
            MenuStepper stepper = MenuStepper.Create(null, parent, "Teams", 3, 2, 8, _ => { });

            Assert.AreEqual("3", stepper.ValueLabel.text);
        }

        [Test]
        public void ThePlusChevronReportsOneMore()
        {
            int reported = -1;
            MenuStepper stepper = MenuStepper.Create(null, parent, "Teams", 3, 2, 8, v => reported = v);

            stepper.Increase.onClick.Invoke();

            Assert.AreEqual(4, reported);
        }

        [Test]
        public void TheMinusChevronReportsOneLess()
        {
            int reported = -1;
            MenuStepper stepper = MenuStepper.Create(null, parent, "Teams", 3, 2, 8, v => reported = v);

            stepper.Decrease.onClick.Invoke();

            Assert.AreEqual(2, reported);
        }

        /// <summary>
        /// The widget's load-bearing rule: a chevron only ever reports what was asked for. Nothing on
        /// screen moves until a caller decides and calls SetValue.
        /// </summary>
        [Test]
        public void TheValueOnlyChangesWhenTheCallerSaysSo()
        {
            MenuStepper stepper = MenuStepper.Create(null, parent, "Teams", 3, 2, 8, _ => { });

            stepper.Increase.onClick.Invoke();
            Assert.AreEqual("3", stepper.ValueLabel.text,
                            "pressing a chevron must not repaint the row by itself");

            stepper.SetValue(4);
            Assert.AreEqual("4", stepper.ValueLabel.text);
        }

        [Test]
        public void ItStopsAtItsLimits()
        {
            int reported = -1;
            MenuStepper stepper = MenuStepper.Create(null, parent, "Teams", 8, 2, 8, v => reported = v);

            stepper.Increase.onClick.Invoke();
            Assert.AreEqual(8, reported, "already at max, so Increase must clamp rather than overshoot");

            stepper.SetValue(2);
            stepper.Decrease.onClick.Invoke();
            Assert.AreEqual(2, reported, "already at min, so Decrease must clamp rather than undershoot");
        }

        [Test]
        public void ItCanBeShownWithoutBeingUsable()
        {
            MenuStepper stepper = MenuStepper.Create(null, parent, "Teams", 3, 2, 8, _ => { });

            stepper.SetInteractable(false);

            Assert.IsFalse(stepper.Decrease.interactable);
            Assert.IsFalse(stepper.Increase.interactable);
            Assert.IsTrue(stepper.Root.gameObject.activeSelf,
                          "SetInteractable must not hide the row — a caller that wants that can do it separately");
        }

        [Test]
        public void ItFitsInsideTheColumnItIsBuiltFor()
        {
            float totalWidth = MenuStepper.LabelWidth + MenuStepper.ChevronWidth * 2f + MenuStepper.ValueWidth;

            Assert.LessOrEqual(totalWidth, MenuEntry.ColumnWidth);
        }
    }
}
