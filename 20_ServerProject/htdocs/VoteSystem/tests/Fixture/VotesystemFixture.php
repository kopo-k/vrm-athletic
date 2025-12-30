<?php
declare(strict_types=1);

namespace App\Test\Fixture;

use Cake\TestSuite\Fixture\TestFixture;

/**
 * VotesystemFixture
 */
class VotesystemFixture extends TestFixture
{
    /**
     * Table name
     *
     * @var string
     */
    public string $table = 'votesystem';
    /**
     * Init method
     *
     * @return void
     */
    public function init(): void
    {
        $this->records = [
            [
                'id' => 1,
                'Name' => 'Lorem ipsum dolor sit amet',
                'Popularity' => 1,
            ],
        ];
        parent::init();
    }
}
