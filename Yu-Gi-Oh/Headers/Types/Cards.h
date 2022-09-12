#pragma once

namespace Cards {
	enum Position {
		Unkown = -1,

		Face_Down_Attack = 0,
		Face_Down_Defense = 1,

		Face_Up_Attack = 256,
		Face_Up_Defense = 257
	};

	class Card {
	public:
		short Id, Picture, Unk;
		Position Position;
	private:
	};

	class Simple_Card {
	public:
		short Id, Picture;
	private:
	};
}