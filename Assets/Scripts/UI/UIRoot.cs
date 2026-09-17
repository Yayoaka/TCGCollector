using UnityEngine;
using UnityEngine.UI;

namespace TCGCollector.UI
{
    /// <summary>
    /// Top-level nav: switches between the Booster, Binder, Quest, Shop and Profile screens.
    /// Lives on the Canvas root built by UISceneBuilder, which also creates the NavBar buttons
    /// this script wires up.
    /// </summary>
    public class UIRoot : MonoBehaviour
    {
        private GameObject _boosterScreen;
        private GameObject _binderScreen;
        private GameObject _questScreen;
        private GameObject _shopScreen;
        private GameObject _profileScreen;
        private BinderScreen _binderScreenComponent;
        private QuestScreen _questScreenComponent;
        private ShopScreen _shopScreenComponent;
        private ProfileScreen _profileScreenComponent;

        private void Awake()
        {
            _boosterScreen = transform.Find("BoosterScreen").gameObject;
            _binderScreen = transform.Find("BinderScreen").gameObject;
            _questScreen = transform.Find("QuestScreen").gameObject;
            _shopScreen = transform.Find("ShopScreen").gameObject;
            _profileScreen = transform.Find("ProfileScreen").gameObject;
            _binderScreenComponent = _binderScreen.GetComponent<BinderScreen>();
            _questScreenComponent = _questScreen.GetComponent<QuestScreen>();
            _shopScreenComponent = _shopScreen.GetComponent<ShopScreen>();
            _profileScreenComponent = _profileScreen.GetComponent<ProfileScreen>();

            transform.Find("NavBar/Boosters Button").GetComponent<Button>().onClick.AddListener(ShowBooster);
            transform.Find("NavBar/Classeur Button").GetComponent<Button>().onClick.AddListener(ShowBinder);
            transform.Find("NavBar/Quetes Button").GetComponent<Button>().onClick.AddListener(ShowQuests);
            transform.Find("NavBar/Boutique Button").GetComponent<Button>().onClick.AddListener(ShowShop);
            transform.Find("NavBar/Profil Button").GetComponent<Button>().onClick.AddListener(ShowProfile);
        }

        private void Start()
        {
            ShowBooster();
        }

        public void ShowBooster()
        {
            _boosterScreen.SetActive(true);
            _binderScreen.SetActive(false);
            _questScreen.SetActive(false);
            _shopScreen.SetActive(false);
            _profileScreen.SetActive(false);
        }

        public void ShowBinder()
        {
            _boosterScreen.SetActive(false);
            _binderScreen.SetActive(true);
            _questScreen.SetActive(false);
            _shopScreen.SetActive(false);
            _profileScreen.SetActive(false);
            if (_binderScreenComponent != null) _binderScreenComponent.Refresh();
        }

        public void ShowQuests()
        {
            _boosterScreen.SetActive(false);
            _binderScreen.SetActive(false);
            _questScreen.SetActive(true);
            _shopScreen.SetActive(false);
            _profileScreen.SetActive(false);
            if (_questScreenComponent != null) _questScreenComponent.Refresh();
        }

        public void ShowShop()
        {
            _boosterScreen.SetActive(false);
            _binderScreen.SetActive(false);
            _questScreen.SetActive(false);
            _shopScreen.SetActive(true);
            _profileScreen.SetActive(false);
            if (_shopScreenComponent != null) _shopScreenComponent.Refresh();
        }

        public void ShowProfile()
        {
            _boosterScreen.SetActive(false);
            _binderScreen.SetActive(false);
            _questScreen.SetActive(false);
            _shopScreen.SetActive(false);
            _profileScreen.SetActive(true);
            if (_profileScreenComponent != null) _profileScreenComponent.Refresh();
        }
    }
}
